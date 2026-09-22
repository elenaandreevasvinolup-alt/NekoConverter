using System.Text.Json;
using System.Text.Json.Serialization;

namespace NekoConverter.Core.Packaging;

/// <summary>
/// Контекст сериализации для Native AOT.
/// Рефлексивная сериализация в AOT-сборке отключена, поэтому разбор манифестов
/// и каталога идёт через исходный генератор.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(PackageCatalog))]
internal sealed partial class PackageJsonContext : JsonSerializerContext
{
}

/// <summary>Чтение и запись манифестов пакетов и каталога.</summary>
public static class PackageJson
{
    /// <summary>
    /// Читает данные по пути, а если файла нет — из встроенного ресурса.
    ///
    /// Так одно и то же работает и на десктопе (файл рядом с приложением, его можно
    /// править без перекомпиляции), и на Android (файл внутри APK, доступен только
    /// как ресурс сборки).
    /// </summary>
    public static string ReadData(string path, string embeddedName)
    {
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        var assembly = typeof(PackageJson).Assembly;
        using var stream = assembly.GetManifestResourceStream(embeddedName)
            ?? throw new FileNotFoundException(
                $"Neither the file \"{path}\" nor the embedded resource \"{embeddedName}\" was found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static PackageManifest LoadManifest(string path) =>
        ParseManifest(File.ReadAllText(path));

    public static PackageManifest ParseManifest(string json) =>
        JsonSerializer.Deserialize(json, PackageJsonContext.Default.PackageManifest)
        ?? throw new InvalidDataException("Empty package manifest.");

    public static string SerializeManifest(PackageManifest manifest) =>
        JsonSerializer.Serialize(manifest, PackageJsonContext.Default.PackageManifest);

    public static PackageCatalog LoadCatalog(string path) =>
        ParseCatalog(File.ReadAllText(path));

    public static PackageCatalog ParseCatalog(string json) =>
        JsonSerializer.Deserialize(json, PackageJsonContext.Default.PackageCatalog)
        ?? throw new InvalidDataException("Empty package catalog.");
}
