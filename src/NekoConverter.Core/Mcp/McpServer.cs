using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NekoConverter.Core.Formats;
using NekoConverter.Core.Packaging;

namespace NekoConverter.Core.Mcp;

/// <summary>
/// MCP-сервер: JSON-RPC 2.0 поверх стандартных потоков ввода-вывода.
///
/// Зачем именно stdio, а не HTTP: MCP-клиенты (редакторы, агенты) запускают
/// сервер как дочерний процесс и общаются с ним по трубам. Это не требует
/// ни портов, ни разрешений брандмауэра, ни запущенного рядом демона.
///
/// ВАЖНО: в stdout уходит ТОЛЬКО протокол. Любые сообщения для человека —
/// в stderr, иначе клиент не разберёт поток.
///
/// Что умеет сервер: преобразовывать файлы теми же движками, что и приложение,
/// показывать доступные форматы и управлять пакетами. Логика не дублируется:
/// всё идёт через ConversionService.
/// </summary>
/// <summary>Контекст сериализации MCP для Native AOT.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    // Поля со значением null не пишем: в JSON-RPC отсутствие «error» и «error: null»
    // означают одно и то же, но второе заставляет клиентов проверять на null.
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
// Регистрируются ВСЕ типы, которые уходят в провод. Пропущенный тип в AOT-сборке
// даёт не ошибку компиляции, а отказ во время работы — именно так и случилось
// с inspect, который вернул пустоту вместо JSON.
[System.Text.Json.Serialization.JsonSerializable(typeof(McpServer.InitializeResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpServer.ToolsListResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpServer.ToolCallResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpServer.RpcEnvelope))]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpServer.ConvertResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpServer.InspectResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpServer.ListFormatsResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpServer.ListPackagesResult))]
internal sealed partial class McpJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

public static class McpServer
{
    private const string ProtocolVersion = "2024-11-05";
    private const string ServerName = "nekoconverter";
    private const string ServerVersion = "1.0.0";

    public static async Task<int> RunAsync()
    {
        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin, Encoding.UTF8);
        await using var stdout = Console.OpenStandardOutput();
        await using var writer = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };

        // Построчный JSON-RPC: один объект на строку. Так работает транспорт stdio.
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string? response;

            try
            {
                response = await HandleAsync(line);
            }
            catch (Exception ex)
            {
                // Ошибка разбора не должна ронять сервер.
                response = ErrorResponse(null, -32700, $"Parse error: {ex.Message}");
            }

            if (response is not null)
            {
                await writer.WriteLineAsync(response);
            }
        }

        return 0;
    }

    private static async Task<string?> HandleAsync(string line)
    {
        JsonElement request;

        try
        {
            request = JsonDocument.Parse(line).RootElement;
        }
        catch (JsonException ex)
        {
            return ErrorResponse(null, -32700, $"Parse error: {ex.Message}");
        }

        var id = request.TryGetProperty("id", out var idElement) ? idElement : (JsonElement?)null;
        var method = request.TryGetProperty("method", out var methodElement)
            ? methodElement.GetString()
            : null;

        if (method is null)
        {
            return ErrorResponse(id, -32600, "Missing method");
        }

        // Уведомления (без id) не требуют ответа.
        var isNotification = id is null;

        switch (method)
        {
            case "initialize":
                return isNotification ? null : Result(id, new InitializeResult(
                    ProtocolVersion,
                    new Capabilities(new EmptyObject()),
                    new ServerInfo(ServerName, ServerVersion)));

            case "notifications/initialized":
                return null;

            case "ping":
                return isNotification ? null : Result(id, new ToolCallResult([], false));

            case "tools/list":
                return isNotification ? null : Result(id, new ToolsListResult(Tools));

            case "tools/call":
                return isNotification ? null : await CallToolAsync(id, request);

            default:
                return isNotification
                    ? null
                    : ErrorResponse(id, -32601, $"Method not found: {method}");
        }
    }

    // ─────────────────────────── Описания инструментов ───────────────────────────

    /// <summary>Описание свойства во входной схеме инструмента.</summary>
    internal sealed record SchemaProperty(string Type, string? Description);

    internal sealed record ToolSchema(string Type, Dictionary<string, SchemaProperty> Properties, string[] Required);

    internal sealed record ToolDefinition(string Name, string Description, ToolSchema InputSchema);

    internal sealed record ToolsListResult(ToolDefinition[] Tools);

    // ─────────────────────────── Результаты инструментов ───────────────────────────

    internal sealed record ContentItem(string Type, string Text);

    internal sealed record ToolCallResult(ContentItem[] Content, bool IsError);

    internal sealed record ConvertResult(string Output, long Bytes, double Milliseconds, string Engine);

    internal sealed record InspectResult(
        string Path,
        string? Detected,
        string? Label,
        string? Kind,
        bool? Readable,
        string? Engine,
        string[] Targets,
        string? Message);

    internal sealed record FormatInfo(string Id, string Kind, string? Label, bool Read, bool Write, string Engine);

    internal sealed record ListFormatsResult(int Total, int Available, FormatInfo[] Formats);

    internal sealed record PackageInfo(
        string Id,
        string Kind,
        string Name,
        string Version,
        double SizeMb,
        bool Installed,
        string? Source,
        bool Permanent,
        string[] Requires,
        string[] Provides);

    internal sealed record ListPackagesResult(PackageInfo[] Packages, string Note);

    internal sealed record ServerInfo(string Name, string Version);

    /// <summary>Пустой объект: в протоколе так выглядит объявление возможностей.</summary>
    internal sealed record EmptyObject;

    internal sealed record Capabilities(EmptyObject Tools);

    internal sealed record InitializeResult(string ProtocolVersion, Capabilities Capabilities, ServerInfo ServerInfo);

    internal sealed record RpcError(int Code, string Message);

    /// <summary>
    /// Конверт ответа JSON-RPC. Полезная нагрузка передаётся уже разобранным
    /// JsonElement: так один тип обслуживает все инструменты, и не нужны
    /// ни обобщения, ни отражение.
    /// </summary>
    internal sealed record RpcEnvelope(string Jsonrpc, JsonElement? Id, JsonElement? Result, RpcError? Error);

    private static readonly ToolDefinition[] Tools =
    [
        new(
            "convert",
            "Convert a file to another format. Returns the output path, size and elapsed time.",
            new ToolSchema("object", new Dictionary<string, SchemaProperty>
            {
                ["input"] = new("string", "Absolute path to the source file"),
                ["target"] = new("string", "Target format id: png, jpg, mp3, pdf, srt, xlsx, and so on"),
                ["output"] = new("string", "Optional output path"),
                ["quality"] = new("integer", "Quality 1-100 for lossy formats"),
                ["maxDimension"] = new("integer", "Limit the longest side of an image, in pixels"),
                ["sampleRate"] = new("integer", "Audio sample rate, Hz"),
                ["channels"] = new("integer", "Number of audio channels"),
                ["bitsPerSample"] = new("integer", "Audio bit depth"),
            }, ["input", "target"])),

        new(
            "inspect",
            "Detect the file format and list the formats it can be converted to right now.",
            new ToolSchema("object", new Dictionary<string, SchemaProperty>
            {
                ["path"] = new("string", "Absolute path to the file"),
            }, ["path"])),

        new(
            "list_formats",
            "List supported formats with their availability.",
            new ToolSchema("object", new Dictionary<string, SchemaProperty>
            {
                ["kind"] = new("string", "Category: Image, Audio, Video, Document, Data, Subtitle, Model3D"),
                ["availableOnly"] = new("boolean", "Only formats available right now"),
            }, [])),

        new(
            "list_packages",
            "Show installed dependency packages and those available for installation.",
            new ToolSchema("object", new Dictionary<string, SchemaProperty>(), [])),
    ];

    private static async Task<string> CallToolAsync(JsonElement? id, JsonElement request)
    {
        if (!request.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("name", out var nameElement))
        {
            return ErrorResponse(id, -32602, "Missing params.name");
        }

        var toolName = nameElement.GetString();
        var arguments = parameters.TryGetProperty("arguments", out var args) ? args : default;

        try
        {
            var text = toolName switch
            {
                "convert" => RunConvert(arguments),
                "inspect" => RunInspect(arguments),
                "list_formats" => RunListFormats(arguments),
                "list_packages" => await RunListPackagesAsync(),
                _ => throw new InvalidOperationException($"Unknown tool: {toolName}"),
            };

            return Result(id, new ToolCallResult([new ContentItem("text", text)], false));
        }
        catch (Exception ex)
        {
            // Ошибка инструмента — это не ошибка протокола: возвращаем как результат
            // с isError, чтобы клиент показал её человеку.
            return Result(id, new ToolCallResult(
                [new ContentItem("text", $"{ex.GetType().Name}: {ex.Message}")], true));
        }
    }

    private static string RunConvert(JsonElement args)
    {
        var input = RequiredString(args, "input");
        var target = RequiredString(args, "target");

        var request = new ConversionRequest(
            input,
            OptionalString(args, "output"),
            target,
            OptionalInt(args, "quality") ?? 90,
            OptionalInt(args, "sampleRate"),
            OptionalInt(args, "channels"),
            OptionalInt(args, "bitsPerSample"),
            OptionalInt(args, "maxDimension"));

        var result = ConversionService.Run(request);

        return Serialize(new ConvertResult(
            result.OutputPath,
            result.Bytes,
            Math.Round(result.Elapsed.TotalMilliseconds),
            result.Engine));
    }

    private static string RunInspect(JsonElement args)
    {
        var path = RequiredString(args, "path");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("File not found.", path);
        }

        var registry = ConversionService.Registry;
        var source = registry.ByPath(path);

        if (source is null)
        {
            return Serialize(new InspectResult(
                path, null, null, null, null, null, [],
                "Extension not recognized."));
        }

        var targets = registry.For(source.Kind)
            .Where(registry.CanWriteNow)
            .Select(f => f.Id)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return Serialize(new InspectResult(
            path, source.Id, source.Label, source.Kind.ToString(),
            registry.CanReadNow(source), source.Engine, targets, null));
    }

    private static string RunListFormats(JsonElement args)
    {
        var registry = ConversionService.Registry;

        FormatKind? kind = null;
        var kindText = OptionalString(args, "kind");
        if (kindText is { Length: > 0 } && Enum.TryParse<FormatKind>(kindText, ignoreCase: true, out var parsed))
        {
            kind = parsed;
        }

        var availableOnly = args.ValueKind == JsonValueKind.Object &&
                            args.TryGetProperty("availableOnly", out var flag) &&
                            flag.ValueKind == JsonValueKind.True;

        var formats = registry.All
            .Where(f => kind is null || f.Kind == kind)
            .Where(f => !availableOnly || registry.IsAvailable(f))
            .OrderBy(f => f.Kind)
            .ThenBy(f => f.Id)
            .Select(f => new FormatInfo(
                f.Id, f.Kind.ToString(), f.Label,
                registry.CanReadNow(f), registry.CanWriteNow(f), f.Engine))
            .ToArray();

        return Serialize(new ListFormatsResult(
            registry.All.Count,
            registry.All.Count(registry.IsAvailable),
            formats));
    }

    private static Task<string> RunListPackagesAsync()
    {
        var manager = ConversionService.Packages;
        var installed = manager.ScanInstalled()
            .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        var packages = ConversionService.Catalog.Packages.Select(p =>
        {
            var id = p.Id ?? "?";
            var has = installed.TryGetValue(id, out var onDisk);

            return new PackageInfo(
                id, p.Kind, p.DisplayName(), p.Version,
                Math.Round(p.SizeBytes / 1048576.0, 1),
                has,
                has ? onDisk!.Source.ToString() : null,
                p.Permanent,
                p.Requires.ToArray(),
                p.ProvidesEngines.ToArray());
        }).ToArray();

        return Task.FromResult(Serialize(new ListPackagesResult(
            packages,
            "Dependency packages are installed once and are not removed together with format packages.")));
    }

    // ─────────────────────────── Разбор аргументов ───────────────────────────

    private static string RequiredString(JsonElement args, string name) =>
        OptionalString(args, name) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"Required parameter \"{name}\" is missing.");

    private static string? OptionalString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(name, out var element) &&
        element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static int? OptionalInt(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(element.GetString(), out var value) => value,
            _ => null,
        };
    }

    // ─────────────────────────── JSON-RPC ───────────────────────────

    /// <summary>
    /// Сериализует полезную нагрузку через исходный генератор.
    /// Отражение здесь недопустимо: в AOT-сборке оно отключено, и первая же
    /// версия сервера падала именно на этом.
    /// </summary>
    private static string Serialize<T>(T value) where T : class
    {
        var typeInfo = McpJsonContext.Default.GetTypeInfo(typeof(T));

        if (typeInfo is null)
        {
            throw new InvalidOperationException(
                "Type " + typeof(T).Name + " is not registered in McpJsonContext. " +
                "Add it to JsonSerializable, otherwise serialization is unavailable in the AOT build.");
        }

        return JsonSerializer.Serialize(value, typeInfo);
    }

    private static string Result(JsonElement? id, object payload)
    {
        // Полезная нагрузка сначала превращается в JsonElement, а затем кладётся
        // в общий конверт: так один тип конверта обслуживает все инструменты.
        var json = payload switch
        {
            InitializeResult typed => JsonSerializer.Serialize(typed, McpJsonContext.Default.InitializeResult),
            ToolsListResult typed => JsonSerializer.Serialize(typed, McpJsonContext.Default.ToolsListResult),
            ToolCallResult typed => JsonSerializer.Serialize(typed, McpJsonContext.Default.ToolCallResult),
            _ => throw new InvalidOperationException($"Unknown response type: {payload.GetType().Name}"),
        };

        var element = JsonDocument.Parse(json).RootElement.Clone();

        return JsonSerializer.Serialize(
            new RpcEnvelope("2.0", id?.Clone(), element, null),
            McpJsonContext.Default.RpcEnvelope);
    }

    private static string ErrorResponse(JsonElement? id, int code, string message) =>
        JsonSerializer.Serialize(
            new RpcEnvelope("2.0", id?.Clone(), null, new RpcError(code, message)),
            McpJsonContext.Default.RpcEnvelope);
}
