using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NekoConverter.Core.Engines.Subtitles;

/// <summary>Одна реплика субтитров с абсолютным временем.</summary>
public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Конвертация субтитров. Написано вручную: все эти форматы — обычный текст,
/// поэтому библиотека не нужна и модуль не нужен, а размер кода около нуля.
///
/// Поддерживаются: SubRip (srt), WebVTT (vtt), Advanced SubStation Alpha (ass/ssa),
/// MicroDVD (sub) и TTML/DFXP.
///
/// MicroDVD хранит время в кадрах, а не в секундах, поэтому при чтении и записи
/// нужен fps. По умолчанию 25 — это значение исторически используется в MicroDVD.
/// </summary>
public static class SubtitleConverter
{
    public static readonly string[] SupportedFormats = ["srt", "vtt", "ass", "ssa", "sub", "ttml", "dfxp"];

    private static readonly Regex AssDialogueField = new(@"^\s*Dialogue\s*:\s*(?<body>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MicroDvdLine = new(@"^\s*\{(?<start>\d+)\}\{(?<end>\d+)\}(?<text>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex TimeArrow = new(
        @"(?<sh>\d{1,3}):(?<sm>\d{2}):(?<ss>\d{2})[,.](?<sf>\d{1,3})\s*-->\s*(?<eh>\d{1,3}):(?<em>\d{2}):(?<es>\d{2})[,.](?<ef>\d{1,3})",
        RegexOptions.Compiled);

    /// <summary>Нормализует идентификатор формата: синонимы сводятся к каноническому.</summary>
    public static string NormalizeFormat(string formatId)
    {
        var id = formatId.TrimStart('.').ToLowerInvariant();
        return id switch
        {
            "ssa" => "ass",
            "dfxp" => "ttml",
            "microdvd" => "sub",
            "aifc" => "aiff",
            _ => id,
        };
    }

    public static void ConvertFile(string inputPath, string outputPath, string targetFormatId, double fps = 25.0)
    {
        var text = ReadText(inputPath);
        var sourceFormat = NormalizeFormat(Path.GetExtension(inputPath));
        var cues = Parse(text, sourceFormat, fps);

        var output = Write(cues, NormalizeFormat(targetFormatId), fps);

        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullOutput, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ReadText(string path)
    {
        var text = File.ReadAllText(path);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    // ─────────────────────────────── Чтение ───────────────────────────────

    public static List<SubtitleCue> Parse(string text, string format, double fps)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        return format switch
        {
            "srt" => ParseSrt(normalized),
            "vtt" => ParseVtt(normalized),
            "ass" => ParseAss(normalized),
            "sub" => ParseMicroDvd(normalized, fps),
            "ttml" => ParseTtml(normalized),
            _ => throw new NotSupportedException($"Reading the \"{format}\" subtitle format is not supported."),
        };
    }

    private static List<SubtitleCue> ParseSrt(string text)
    {
        var cues = new List<SubtitleCue>();

        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n', StringSplitOptions.None);
            var arrowIndex = Array.FindIndex(lines, l => l.Contains("-->"));
            if (arrowIndex < 0)
            {
                continue;
            }

            if (TryParseArrow(lines[arrowIndex], out var start, out var end))
            {
                cues.Add(new SubtitleCue(start, end, string.Join('\n', lines[(arrowIndex + 1)..]).Trim()));
            }
        }

        return cues;
    }

    private static List<SubtitleCue> ParseVtt(string text)
    {
        // WEBVTT и заголовки NOTE/STYLE пропускаем: у них нет стрелки времени.
        var body = text.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)
            ? text[(text.IndexOf('\n') + 1)..]
            : text;

        return ParseSrt(body);
    }

    private static List<SubtitleCue> ParseAss(string text)
    {
        var cues = new List<SubtitleCue>();
        var inEvents = false;
        var startIndex = -1;
        var endIndex = -1;
        var textIndex = -1;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith('['))
            {
                inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEvents)
            {
                continue;
            }

            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                // Положение Start/End/Text задаётся строкой Format, а не фиксировано:
                // у SSA-стилей порядок полей отличается от ASS. Берём индексы из заголовка.
                var fields = line["Format:".Length..].Split(',')
                    .Select(f => f.Trim().ToLowerInvariant())
                    .ToList();

                startIndex = fields.IndexOf("start");
                endIndex = fields.IndexOf("end");
                textIndex = fields.IndexOf("text");
                continue;
            }

            var match = AssDialogueField.Match(line);
            if (!match.Success || startIndex < 0 || endIndex < 0 || textIndex < 0)
            {
                continue;
            }

            // Текст — последнее поле и сам может содержать запятые,
            // поэтому режем только первые textIndex полей.
            var parts = match.Groups["body"].Value.Split(',');
            if (parts.Length <= textIndex)
            {
                continue;
            }

            if (!TryParseAssTime(parts[startIndex].Trim(), out var start) ||
                !TryParseAssTime(parts[endIndex].Trim(), out var end))
            {
                continue;
            }

            var body = string.Join(',', parts[textIndex..])
                .Replace("\\N", "\n", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Trim();

            cues.Add(new SubtitleCue(start, end, body));
        }

        return cues;
    }

    private static List<SubtitleCue> ParseMicroDvd(string text, double fps)
    {
        var cues = new List<SubtitleCue>();
        var safeFps = fps > 0 ? fps : 25.0;

        foreach (var line in text.Split('\n'))
        {
            var match = MicroDvdLine.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var startFrame = double.Parse(match.Groups["start"].Value, CultureInfo.InvariantCulture);
            var endFrame = double.Parse(match.Groups["end"].Value, CultureInfo.InvariantCulture);

            var body = match.Groups["text"].Value
                .Replace("|", "\n", StringComparison.Ordinal)
                .Trim();

            cues.Add(new SubtitleCue(
                TimeSpan.FromSeconds(startFrame / safeFps),
                TimeSpan.FromSeconds(endFrame / safeFps),
                body));
        }

        return cues;
    }

    private static List<SubtitleCue> ParseTtml(string text)
    {
        var cues = new List<SubtitleCue>();

        XDocument document;
        try
        {
            document = XDocument.Parse(text);
        }
        catch
        {
            return cues;
        }

        foreach (var paragraph in document.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            var begin = (string?)paragraph.Attribute("begin");
            var end = (string?)paragraph.Attribute("end");
            var duration = (string?)paragraph.Attribute("dur");

            if (begin is null || !TryParseTtmlTime(begin, out var start))
            {
                continue;
            }

            var stop = start;
            if (end is not null && TryParseTtmlTime(end, out var parsedEnd))
            {
                stop = parsedEnd;
            }
            else if (duration is not null && TryParseTtmlTime(duration, out var parsedDuration))
            {
                stop = start + parsedDuration;
            }

            var body = string.Concat(paragraph.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
            if (body.Length > 0)
            {
                cues.Add(new SubtitleCue(start, stop, body));
            }
        }

        return cues;
    }

    // ─────────────────────────────── Запись ───────────────────────────────

    public static string Write(IReadOnlyList<SubtitleCue> cues, string format, double fps)
    {
        return format switch
        {
            "srt" => WriteSrt(cues),
            "vtt" => WriteVtt(cues),
            "ass" => WriteAss(cues),
            "sub" => WriteMicroDvd(cues, fps > 0 ? fps : 25.0),
            "ttml" => WriteTtml(cues),
            _ => throw new NotSupportedException($"Writing the \"{format}\" subtitle format is not supported."),
        };
    }

    private static string WriteSrt(IReadOnlyList<SubtitleCue> cues)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            builder.Append(i + 1).Append('\n');
            builder.Append(FormatTime(cue.Start, ",", 3)).Append(" --> ").Append(FormatTime(cue.End, ",", 3)).Append('\n');
            builder.Append(cue.Text).Append("\n\n");
        }

        return builder.ToString();
    }

    private static string WriteVtt(IReadOnlyList<SubtitleCue> cues)
    {
        var builder = new StringBuilder("WEBVTT\n\n");

        foreach (var cue in cues)
        {
            builder.Append(FormatTime(cue.Start, ".", 3)).Append(" --> ").Append(FormatTime(cue.End, ".", 3)).Append('\n');
            builder.Append(cue.Text).Append("\n\n");
        }

        return builder.ToString();
    }

    private static string WriteAss(IReadOnlyList<SubtitleCue> cues)
    {
        var builder = new StringBuilder();

        builder.Append("[Script Info]\n");
        builder.Append("ScriptType: v4.00+\n");
        builder.Append("WrapStyle: 0\n");
        builder.Append("ScaledBorderAndShadow: yes\n\n");

        builder.Append("[V4+ Styles]\n");
        builder.Append("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n");
        builder.Append("Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1\n\n");

        builder.Append("[Events]\n");
        builder.Append("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");

        foreach (var cue in cues)
        {
            var body = cue.Text.Replace("\n", "\\N", StringComparison.Ordinal);
            builder.Append("Dialogue: 0,")
                .Append(FormatTime(cue.Start, ".", 2))
                .Append(',')
                .Append(FormatTime(cue.End, ".", 2))
                .Append(",Default,,0,0,0,,")
                .Append(body)
                .Append('\n');
        }

        return builder.ToString();
    }

    private static string WriteMicroDvd(IReadOnlyList<SubtitleCue> cues, double fps)
    {
        var builder = new StringBuilder();
        // Первая строка MicroDVD задаёт fps в виде {1}{1}25.000
        builder.Append("{1}{1}").Append(fps.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');

        foreach (var cue in cues)
        {
            var start = (int)Math.Round(cue.Start.TotalSeconds * fps);
            var end = (int)Math.Round(cue.End.TotalSeconds * fps);
            var body = cue.Text.Replace("\n", "|", StringComparison.Ordinal);

            builder.Append('{').Append(start).Append("}{").Append(end).Append('}').Append(body).Append('\n');
        }

        return builder.ToString();
    }

    private static string WriteTtml(IReadOnlyList<SubtitleCue> cues)
    {
        XNamespace ttml = "http://www.w3.org/ns/ttml";

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ttml + "tt",
                new XAttribute(XNamespace.Xml + "lang", "zh"),
                new XElement(ttml + "body",
                    new XElement(ttml + "div",
                        cues.Select(cue =>
                            new XElement(ttml + "p",
                                new XAttribute("begin", FormatTime(cue.Start, ".", 3)),
                                new XAttribute("end", FormatTime(cue.End, ".", 3)),
                                cue.Text))))));

        return document.Declaration + "\n" + document;
    }

    // ─────────────────────────────── Время ───────────────────────────────

    private static string FormatTime(TimeSpan value, string separator, int fractionDigits)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var fraction = fractionDigits switch
        {
            2 => (int)Math.Round(value.Milliseconds / 10.0),
            _ => value.Milliseconds,
        };

        var format = fractionDigits == 2 ? "D2" : "D3";
        return $"{value.Hours:D2}:{value.Minutes:D2}:{value.Seconds:D2}{separator}{fraction.ToString(format, CultureInfo.InvariantCulture)}";
    }

    private static bool TryParseArrow(string line, out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;

        var match = TimeArrow.Match(line);
        if (!match.Success)
        {
            return false;
        }

        start = Build(
            match.Groups["sh"].Value, match.Groups["sm"].Value, match.Groups["ss"].Value, match.Groups["sf"].Value);
        end = Build(
            match.Groups["eh"].Value, match.Groups["em"].Value, match.Groups["es"].Value, match.Groups["ef"].Value);

        return true;
    }

    private static TimeSpan Build(string hours, string minutes, string seconds, string fraction)
    {
        // Доли секунды бывают двузначными (центисекунды) и трёхзначными (миллисекунды).
        var milliseconds = fraction.Length switch
        {
            1 => int.Parse(fraction, CultureInfo.InvariantCulture) * 100,
            2 => int.Parse(fraction, CultureInfo.InvariantCulture) * 10,
            _ => int.Parse(fraction[..3], CultureInfo.InvariantCulture),
        };

        return new TimeSpan(
            0,
            int.Parse(hours, CultureInfo.InvariantCulture),
            int.Parse(minutes, CultureInfo.InvariantCulture),
            int.Parse(seconds, CultureInfo.InvariantCulture),
            milliseconds);
    }

    private static bool TryParseAssTime(string value, out TimeSpan result)
    {
        result = default;

        var parts = value.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        result = new TimeSpan(0, hours, minutes, 0, 0) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static bool TryParseTtmlTime(string value, out TimeSpan result)
    {
        result = default;

        // Поддерживаем два вида: "00:00:01.500" и "1.5s" / "1500ms".
        if (value.EndsWith('s'))
        {
            var number = value.TrimEnd('s');
            var multiplier = value.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ? 0.001 : 1.0;
            number = number.TrimEnd('m');

            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                result = TimeSpan.FromSeconds(seconds * multiplier);
                return true;
            }

            return false;
        }

        var parts = value.Split(':');
        if (parts.Length < 3)
        {
            return false;
        }

        var fraction = parts[2].Split('.', ',');
        var wholeSeconds = int.Parse(fraction[0], CultureInfo.InvariantCulture);
        var milliseconds = fraction.Length > 1
            ? (int)Math.Round(double.Parse("0." + fraction[1], CultureInfo.InvariantCulture) * 1000)
            : 0;

        result = new TimeSpan(
            0,
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            wholeSeconds,
            milliseconds);

        return true;
    }
}
