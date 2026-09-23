using NekoConverter.Core.Preview;
using Avalonia;
using Avalonia.Styling;

// Под Android есть собственный Android.App.Application — псевдоним убирает
// неоднозначность при сборке под эту платформу.
using Application = Avalonia.Application;

namespace NekoConverter.App;

/// <summary>Как выбирается тема оформления.</summary>
public enum ThemeMode
{
    Light,
    Dark,
    System,
    ByTime,
}

/// <summary>
/// Настройки приложения.
///
/// Хранятся в простом текстовом формате «ключ=значение», а не в JSON.
/// Причина: в AOT-сборке рефлексивная сериализация System.Text.Json отключена,
/// а ради двух полей тянуть исходный генератор незачем.
/// </summary>
public sealed class AppSettings
{
    private const string ThemeKey = "theme";
    private const string LanguageKey = "language";
    private const string PreviewersKey = "previewers";

    /// <summary>
    /// Принудительная тема для режима предпросмотра (--theme). Если задана,
    /// настройки из файла игнорируются — нужно, чтобы снять обе темы подряд.
    /// </summary>
    public static ThemeMode? ForceTheme { get; set; }

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>
    /// Язык по умолчанию — английский: он понятен шире остальных,
    /// а остальные языки подхватываются из файлов.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Какие предпросмотрщики включены. По умолчанию все: они помогают убедиться,
    /// что выбран нужный файл, и на обычных файлах почти не стоят времени.
    /// </summary>
    public Previewers Previewers { get; set; } = Previewers.All;

    /// <summary>Каталог пользовательских данных: здесь же будут лежать модули.</summary>
    public static string DataDirectory
    {
        get
        {
            var path = OperatingSystem.IsMacOS()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "NekoConverter")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NekoConverter");

            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string ModulesDirectory => Path.Combine(DataDirectory, "modules");

    private static string SettingsPath => Path.Combine(DataDirectory, "settings.conf");

    public static AppSettings Load()
    {
        var settings = new AppSettings();

        try
        {
            if (!File.Exists(SettingsPath))
            {
                return settings;
            }

            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();

                switch (key)
                {
                    case ThemeKey when Enum.TryParse<ThemeMode>(value, ignoreCase: true, out var mode):
                        settings.Theme = mode;
                        break;

                    case LanguageKey when value.Length > 0:
                        settings.Language = value;
                        break;

                    case PreviewersKey when int.TryParse(value, out var previewers):
                        settings.Previewers = (Previewers)previewers;
                        break;
                }
            }
        }
        catch
        {
            // Битый файл настроек не должен мешать запуску — берём значения по умолчанию.
        }

        return settings;
    }

    public void Save()
    {
        try
        {
            File.WriteAllLines(SettingsPath,
            [
                $"{ThemeKey}={Theme}",
                $"{LanguageKey}={Language}",
                $"{PreviewersKey}={(int)Previewers}",
            ]);
        }
        catch
        {
            // Настройки не критичны: если записать не удалось, просто работаем дальше.
        }
    }

    /// <summary>
    /// Применяет тему к приложению.
    /// ByTime — тёмная тема с 19:00 до 07:00 по местному времени.
    /// </summary>
    public static void ApplyTheme(ThemeMode mode)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            ThemeMode.System => ThemeVariant.Default,
            ThemeMode.ByTime => IsNightNow() ? ThemeVariant.Dark : ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }

    public static bool IsNightNow()
    {
        var hour = DateTime.Now.Hour;
        return hour >= 19 || hour < 7;
    }

    /// <summary>Тема, которая реально применена сейчас (с учётом системной и времени).</summary>
    public static bool IsEffectivelyDark(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => false,
        ThemeMode.Dark => true,
        ThemeMode.ByTime => IsNightNow(),
        _ => Application.Current?.ActualThemeVariant == ThemeVariant.Dark,
    };
}
