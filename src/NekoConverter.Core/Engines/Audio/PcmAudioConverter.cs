using System.Buffers.Binary;
using System.Text;

namespace NekoConverter.Core.Engines.Audio;

/// <summary>Параметры звука для чтения и записи.</summary>
/// <param name="SampleRate">Частота дискретизации, Гц.</param>
/// <param name="Channels">Число каналов.</param>
/// <param name="BitsPerSample">Разрядность для целочисленных форматов (8/16/24/32).</param>
/// <param name="IsFloat">true — 32-битный float, false — целые.</param>
public sealed record AudioFormat(int SampleRate, int Channels, int BitsPerSample = 16, bool IsFloat = false);

/// <summary>Разобранный звук: частота, каналы и сэмплы в float [-1, 1].</summary>
public sealed record AudioData(int SampleRate, int Channels, float[] Samples)
{
    public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;

    public TimeSpan Duration => TimeSpan.FromSeconds(SampleRate > 0 ? (double)FrameCount / SampleRate : 0);
}

/// <summary>
/// Конвертация несжатого звука: WAV, AIFF и AU. Написано вручную — это просто контейнеры
/// вокруг PCM, поэтому библиотека и модуль не нужны.
///
/// Что умеет: смену контейнера, разрядности (8/16/24/32 целые и 32 float),
/// числа каналов (сведение в моно и разведение в стерео) и частоты дискретизации.
///
/// Про ресемплинг: используется линейная интерполяция. Это честно, быстро и достаточно
/// для «быстрого инструмента», но это НЕ студийное качество. Для 44.1↔48 кГц разница
/// на слух практически не заметна, для серьёзной работы нужен модуль с libsoxr.
///
/// Сжатые форматы (MP3, FLAC, AAC) сюда не входят — им нужен модуль av.
/// </summary>
public static class PcmAudioConverter
{
    public static readonly string[] SupportedFormats = ["wav", "aiff", "aif", "aifc", "au", "snd"];

    /// <summary>
    /// Форматы, которые обслуживает этот движок: контейнеры вокруг несжатого PCM.
    /// Используется маршрутизатором, чтобы отличить свой быстрый путь от afconvert.
    /// </summary>
    public static bool IsPcmFormat(string formatId) =>
        formatId.TrimStart('.').ToLowerInvariant() is "wav" or "aiff" or "aif" or "aifc" or "au" or "snd";

    public static void ConvertFile(
        string inputPath,
        string outputPath,
        string targetFormatId,
        AudioFormat? target = null)
    {
        var audio = ReadFile(inputPath);

        // Пользователь может задать только часть параметров (например, лишь --bits 24).
        // Незаданные значения ОБЯЗАНЫ браться из источника: 0 в заголовке AIFF/AU даёт
        // формально валидный файл с нулевой частотой и нулём каналов, который потом
        // невозможно прочитать. Раньше это была именно такая ошибка.
        var format = target is null
            ? new AudioFormat(audio.SampleRate, audio.Channels)
            : new AudioFormat(
                target.SampleRate > 0 ? target.SampleRate : audio.SampleRate,
                target.Channels > 0 ? target.Channels : audio.Channels,
                target.BitsPerSample > 0 ? target.BitsPerSample : 16,
                target.IsFloat);

        if (format.SampleRate != audio.SampleRate)
        {
            audio = Resample(audio, format.SampleRate);
        }

        if (format.Channels != audio.Channels)
        {
            audio = Remix(audio, format.Channels);
        }

        var bytes = targetFormatId.TrimStart('.').ToLowerInvariant() switch
        {
            "wav" => WriteWav(audio, format),
            "aiff" or "aif" or "aifc" => WriteAiff(audio, format),
            "au" or "snd" => WriteAu(audio, format),
            _ => throw new NotSupportedException($"Writing audio format \"{targetFormatId}\" is not supported."),
        };

        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullOutput, bytes);
    }

    public static AudioData ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length < 12)
        {
            throw new InvalidDataException("File is too small to be an audio container.");
        }

        if (Match(bytes, "RIFF", 0) && Match(bytes, "WAVE", 8))
        {
            return ReadWav(bytes);
        }

        if (Match(bytes, "FORM", 0) && (Match(bytes, "AIFF", 8) || Match(bytes, "AIFC", 8)))
        {
            return ReadAiff(bytes);
        }

        if (Match(bytes, ".snd", 0))
        {
            return ReadAu(bytes);
        }

        throw new InvalidDataException("Could not detect the audio container (expected WAV, AIFF or AU).");
    }

    // ─────────────────────────────── WAV ───────────────────────────────

    private static AudioData ReadWav(byte[] bytes)
    {
        var position = 12;
        var formatFound = false;
        var sampleRate = 0;
        var channels = 0;
        var bits = 0;
        var isFloat = false;
        byte[]? payload = null;

        while (position + 8 <= bytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, position, 4);
            var chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4, 4));
            var body = position + 8;

            if (chunkId == "fmt " && body + 16 <= bytes.Length)
            {
                var audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 2, 2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 14, 2));

                // 1 = PCM, 3 = IEEE float, 0xFFFE = расширенный формат (смотрим SubFormat).
                isFloat = audioFormat == 3;
                if (audioFormat == 0xFFFE && body + 26 <= bytes.Length)
                {
                    isFloat = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 24, 2)) == 3;
                }

                formatFound = true;
            }
            else if (chunkId == "data")
            {
                var length = Math.Min(chunkSize, bytes.Length - body);
                payload = bytes.AsSpan(body, length).ToArray();
            }

            // Чанки выравниваются на чётную границу.
            position = body + chunkSize + (chunkSize % 2);
        }

        if (!formatFound || payload is null || channels <= 0)
        {
            throw new InvalidDataException("Corrupted WAV: missing the fmt or data chunk.");
        }

        var samples = BytesToFloat(payload, bits, isFloat, littleEndian: true);
        return new AudioData(sampleRate, channels, samples);
    }

    private static byte[] WriteWav(AudioData audio, AudioFormat format)
    {
        var payload = FloatToBytes(audio.Samples, format.BitsPerSample, format.IsFloat, littleEndian: true);
        var bytesPerSample = format.BitsPerSample / 8;
        var byteRate = format.SampleRate * format.Channels * bytesPerSample;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + payload.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((ushort)(format.IsFloat ? 3 : 1));
        writer.Write((ushort)format.Channels);
        writer.Write(format.SampleRate);
        writer.Write(byteRate);
        writer.Write((ushort)(format.Channels * bytesPerSample));
        writer.Write((ushort)format.BitsPerSample);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Flush();

        return stream.ToArray();
    }

    // ─────────────────────────────── AIFF ───────────────────────────────

    private static AudioData ReadAiff(byte[] bytes)
    {
        var position = 12;
        var channels = 0;
        var bits = 0;
        var sampleRate = 0;
        byte[]? payload = null;

        while (position + 8 <= bytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, position, 4);
            var chunkSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 4, 4));
            var body = position + 8;

            if (chunkId == "COMM" && body + 18 <= bytes.Length)
            {
                channels = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(body, 2));
                bits = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(body + 6, 2));
                sampleRate = (int)Math.Round(ReadExtended80(bytes, body + 8));
            }
            else if (chunkId == "SSND" && body + 8 <= bytes.Length)
            {
                // Первые 8 байт SSND — offset и blockSize, дальше сами сэмплы.
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(body, 4));
                var start = body + 8 + offset;
                var length = Math.Min(chunkSize - 8 - offset, bytes.Length - start);

                if (length > 0)
                {
                    payload = bytes.AsSpan(start, length).ToArray();
                }
            }

            position = body + chunkSize + (chunkSize % 2);
        }

        if (payload is null || channels <= 0)
        {
            throw new InvalidDataException("Corrupted AIFF: missing the COMM or SSND chunk.");
        }

        // AIFF всегда big-endian.
        var samples = BytesToFloat(payload, bits, isFloat: false, littleEndian: false);
        return new AudioData(sampleRate, channels, samples);
    }

    private static byte[] WriteAiff(AudioData audio, AudioFormat format)
    {
        var payload = FloatToBytes(audio.Samples, format.BitsPerSample, isFloat: false, littleEndian: false);
        var frames = format.Channels > 0 ? audio.Samples.Length / format.Channels : 0;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // AIFF — формат с БОЛЬШИМ порядком байт во ВСЕХ полях.
        // BinaryWriter пишет int в little-endian, поэтому числа пишем побайтово сами.
        writer.Write(Encoding.ASCII.GetBytes("FORM"));
        WriteUInt32Be(writer, 4 + 26 + 16 + payload.Length);
        writer.Write(Encoding.ASCII.GetBytes("AIFF"));

        writer.Write(Encoding.ASCII.GetBytes("COMM"));
        WriteUInt32Be(writer, 18);
        WriteUInt16Be(writer, format.Channels);
        WriteUInt32Be(writer, frames);
        WriteUInt16Be(writer, format.BitsPerSample);
        writer.Write(EncodeExtended80(format.SampleRate));

        writer.Write(Encoding.ASCII.GetBytes("SSND"));
        WriteUInt32Be(writer, 8 + payload.Length);
        WriteUInt32Be(writer, 0); // offset
        WriteUInt32Be(writer, 0); // blockSize
        writer.Write(payload);
        writer.Flush();

        return stream.ToArray();
    }

    private static void WriteUInt16Be(BinaryWriter writer, int value)
    {
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteUInt32Be(BinaryWriter writer, long value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    /// <summary>Чтение 80-битного расширенного float (формат хранения частоты в AIFF).</summary>
    private static double ReadExtended80(byte[] bytes, int offset)
    {
        var exponent = ((bytes[offset] & 0x7F) << 8) | bytes[offset + 1];
        ulong mantissa = 0;

        for (var i = 0; i < 8; i++)
        {
            mantissa = (mantissa << 8) | bytes[offset + 2 + i];
        }

        if (exponent == 0 && mantissa == 0)
        {
            return 0;
        }

        var sign = (bytes[offset] & 0x80) != 0 ? -1.0 : 1.0;
        return sign * mantissa * Math.Pow(2, exponent - 16383 - 63);
    }

    private static byte[] EncodeExtended80(double value)
    {
        var result = new byte[10];
        if (value <= 0)
        {
            return result;
        }

        var exponent = (int)Math.Floor(Math.Log2(value));
        var mantissaValue = value / Math.Pow(2, exponent); // в диапазоне [1, 2)
        var mantissa = (ulong)Math.Round(mantissaValue * Math.Pow(2, 63));

        // Округление могло вывести за разрядную сетку — нормализуем.
        if (mantissa == 0)
        {
            mantissa = 1UL << 63;
        }

        var biased = exponent + 16383;
        result[0] = (byte)((biased >> 8) & 0x7F);
        result[1] = (byte)(biased & 0xFF);

        for (var i = 0; i < 8; i++)
        {
            result[2 + i] = (byte)((mantissa >> (56 - (8 * i))) & 0xFF);
        }

        return result;
    }

    // ─────────────────────────────── AU ───────────────────────────────

    private static AudioData ReadAu(byte[] bytes)
    {
        var dataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4));
        var encoding = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12, 4));
        var sampleRate = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var channels = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));

        if (dataOffset <= 0 || dataOffset >= bytes.Length || channels <= 0)
        {
            throw new InvalidDataException("Corrupted AU: invalid header.");
        }

        var payload = bytes.AsSpan(dataOffset).ToArray();

        var samples = encoding switch
        {
            1 => MuLawToFloat(payload),                       // 8-бит µ-law
            2 => BytesToFloat(payload, 8, false, false),      // 8-бит линейный
            3 => BytesToFloat(payload, 16, false, false),
            4 => BytesToFloat(payload, 24, false, false),
            5 => BytesToFloat(payload, 32, false, false),
            6 => BytesToFloat(payload, 32, true, false),
            7 => BytesToFloat(payload, 64, true, false),
            _ => throw new InvalidDataException($"AU encoding {encoding} is not supported."),
        };

        return new AudioData(sampleRate, channels, samples);
    }

    private static byte[] WriteAu(AudioData audio, AudioFormat format)
    {
        // AU не умеет float — приводим к целым.
        var bits = format.IsFloat ? 16 : format.BitsPerSample;
        var encoding = bits switch { 8 => 2, 16 => 3, 24 => 4, _ => 5 };
        var payload = FloatToBytes(audio.Samples, bits, isFloat: false, littleEndian: false);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Заголовок AU тоже с БОЛЬШИМ порядком байт — пишем побайтово.
        writer.Write(Encoding.ASCII.GetBytes(".snd"));
        WriteUInt32Be(writer, 24);              // dataOffset
        WriteUInt32Be(writer, payload.Length);
        WriteUInt32Be(writer, encoding);
        WriteUInt32Be(writer, format.SampleRate);
        WriteUInt32Be(writer, format.Channels);
        writer.Write(payload);
        writer.Flush();

        return stream.ToArray();
    }

    // ─────────────────────── Преобразование сэмплов ───────────────────────

    private static float[] BytesToFloat(byte[] payload, int bits, bool isFloat, bool littleEndian)
    {
        var bytesPerSample = Math.Max(1, bits / 8);
        var count = payload.Length / bytesPerSample;
        var samples = new float[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * bytesPerSample;
            samples[i] = bits switch
            {
                8 => (payload[offset] - 128) / 128f,                 // 8-бит PCM беззнаковый
                16 => ReadInt(payload, offset, 2, littleEndian) / 32768f,
                24 => ReadInt(payload, offset, 3, littleEndian) / 8388608f,
                32 => isFloat
                    ? ReadFloat(payload, offset, littleEndian)
                    : ReadInt(payload, offset, 4, littleEndian) / 2147483648f,
                64 => (float)ReadDouble(payload, offset, littleEndian),
                _ => 0f,
            };
        }

        return samples;
    }

    private static byte[] FloatToBytes(float[] samples, int bits, bool isFloat, bool littleEndian)
    {
        var bytesPerSample = Math.Max(1, bits / 8);
        var payload = new byte[samples.Length * bytesPerSample];

        for (var i = 0; i < samples.Length; i++)
        {
            var value = Math.Clamp(samples[i], -1f, 1f);
            var offset = i * bytesPerSample;

            switch (bits)
            {
                case 8:
                    payload[offset] = (byte)Math.Clamp((int)Math.Round(value * 128f) + 128, 0, 255);
                    break;

                // Масштаб ОБЯЗАН совпадать с обратным преобразованием в BytesToFloat,
                // иначе 16-бит PCM перестаёт быть обратимым: делим на 32768, значит
                // и умножаем на 32768, а не на 32767. Верхняя граница обрезается.
                case 16:
                    WriteIntClamped(payload, offset, 2, (long)Math.Round(value * 32768f), littleEndian);
                    break;

                case 24:
                    WriteIntClamped(payload, offset, 3, (long)Math.Round(value * 8388608f), littleEndian);
                    break;

                case 32:
                    if (isFloat)
                    {
                        WriteFloat(payload, offset, value, littleEndian);
                    }
                    else
                    {
                        WriteIntClamped(payload, offset, 4, (long)Math.Round(value * 2147483648f), littleEndian);
                    }

                    break;

                case 64:
                    WriteDouble(payload, offset, value, littleEndian);
                    break;
            }
        }

        return payload;
    }

    /// <summary>
    /// Записывает целое со знаком и обрезает по разрядности: округление на границе
    /// диапазона (например value = 1.0) иначе даёт переполнение.
    /// </summary>
    private static void WriteIntClamped(byte[] buffer, int offset, int size, long value, bool littleEndian)
    {
        var max = (1L << ((size * 8) - 1)) - 1;
        var min = -(1L << ((size * 8) - 1));
        WriteInt(buffer, offset, size, Math.Clamp(value, min, max), littleEndian);
    }

    private static long ReadInt(byte[] buffer, int offset, int size, bool littleEndian)
    {
        long value = 0;

        if (littleEndian)
        {
            for (var i = size - 1; i >= 0; i--)
            {
                value = (value << 8) | buffer[offset + i];
            }
        }
        else
        {
            for (var i = 0; i < size; i++)
            {
                value = (value << 8) | buffer[offset + i];
            }
        }

        // Знаковое расширение: если старший бит установлен, вычитаем разрядность.
        var shift = (8 - (size * 8 % 8)) % 8;
        _ = shift;
        var signBit = 1L << ((size * 8) - 1);
        return (value & signBit) != 0 ? value - (1L << (size * 8)) : value;
    }

    private static void WriteInt(byte[] buffer, int offset, int size, long value, bool littleEndian)
    {
        for (var i = 0; i < size; i++)
        {
            var index = littleEndian ? i : size - 1 - i;
            buffer[offset + index] = (byte)((value >> (8 * i)) & 0xFF);
        }
    }

    private static float ReadFloat(byte[] buffer, int offset, bool littleEndian)
    {
        var span = buffer.AsSpan(offset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadSingleLittleEndian(span)
            : BinaryPrimitives.ReadSingleBigEndian(span);
    }

    private static void WriteFloat(byte[] buffer, int offset, float value, bool littleEndian)
    {
        var span = buffer.AsSpan(offset, 4);
        if (littleEndian)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteSingleBigEndian(span, value);
        }
    }

    private static double ReadDouble(byte[] buffer, int offset, bool littleEndian)
    {
        var span = buffer.AsSpan(offset, 8);
        return littleEndian
            ? BinaryPrimitives.ReadDoubleLittleEndian(span)
            : BinaryPrimitives.ReadDoubleBigEndian(span);
    }

    private static void WriteDouble(byte[] buffer, int offset, double value, bool littleEndian)
    {
        var span = buffer.AsSpan(offset, 8);
        if (littleEndian)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteDoubleBigEndian(span, value);
        }
    }

    /// <summary>Декодирование µ-law — используется в старых AU-файлах.</summary>
    private static float[] MuLawToFloat(byte[] payload)
    {
        var samples = new float[payload.Length];

        for (var i = 0; i < payload.Length; i++)
        {
            var encoded = ~payload[i] & 0xFF;
            var sign = encoded & 0x80;
            var exponent = (encoded >> 4) & 0x07;
            var mantissa = encoded & 0x0F;

            var sample = ((mantissa << 3) + 0x84) << exponent;
            sample -= 0x84;

            samples[i] = (sign != 0 ? -sample : sample) / 32768f;
        }

        return samples;
    }

    // ─────────────────────── Частота и каналы ───────────────────────

    /// <summary>Линейная интерполяция. Быстро и предсказуемо, но не студийное качество.</summary>
    public static AudioData Resample(AudioData audio, int targetRate)
    {
        if (targetRate <= 0 || targetRate == audio.SampleRate || audio.FrameCount == 0)
        {
            return audio;
        }

        var channels = audio.Channels;
        var sourceFrames = audio.FrameCount;
        var targetFrames = (int)Math.Round(sourceFrames * (double)targetRate / audio.SampleRate);
        var result = new float[targetFrames * channels];
        var ratio = (double)audio.SampleRate / targetRate;

        for (var frame = 0; frame < targetFrames; frame++)
        {
            var sourcePosition = frame * ratio;
            var index = (int)sourcePosition;
            var fraction = (float)(sourcePosition - index);
            var next = Math.Min(index + 1, sourceFrames - 1);

            for (var channel = 0; channel < channels; channel++)
            {
                var a = audio.Samples[(index * channels) + channel];
                var b = audio.Samples[(next * channels) + channel];
                result[(frame * channels) + channel] = a + ((b - a) * fraction);
            }
        }

        return new AudioData(targetRate, channels, result);
    }

    /// <summary>Моно сводится усреднением, при расширении каналы дублируются.</summary>
    public static AudioData Remix(AudioData audio, int targetChannels)
    {
        if (targetChannels <= 0 || targetChannels == audio.Channels || audio.FrameCount == 0)
        {
            return audio;
        }

        var sourceChannels = audio.Channels;
        var frames = audio.FrameCount;
        var result = new float[frames * targetChannels];

        for (var frame = 0; frame < frames; frame++)
        {
            var sourceOffset = frame * sourceChannels;

            if (targetChannels == 1)
            {
                var sum = 0f;
                for (var channel = 0; channel < sourceChannels; channel++)
                {
                    sum += audio.Samples[sourceOffset + channel];
                }

                result[frame] = sum / sourceChannels;
                continue;
            }

            for (var channel = 0; channel < targetChannels; channel++)
            {
                // Лишние каналы берём по кругу, недостающие — последним доступным.
                var sourceChannel = channel < sourceChannels ? channel : sourceChannels - 1;
                result[(frame * targetChannels) + channel] = audio.Samples[sourceOffset + sourceChannel];
            }
        }

        return new AudioData(audio.SampleRate, targetChannels, result);
    }

    private static bool Match(byte[] bytes, string magic, int offset) =>
        bytes.Length >= offset + magic.Length &&
        Encoding.ASCII.GetString(bytes, offset, magic.Length) == magic;
}
