using System.Net.Http.Headers;

namespace NekoConverter.Core.Packaging;

/// <summary>
/// Проверка, доступны ли площадки, с которых скачиваются пакеты.
///
/// Зачем это нужно: пакеты-зависимости лежат на зарубежных площадках, и в некоторых
/// сетях они недоступны. Без проверки человек нажимает «Установить», ждёт минуту
/// и получает непонятную ошибку. С проверкой он видит состояние сразу и понимает,
/// что дело в сети, а не в приложении.
///
/// Проверка намеренно неблокирующая: недоступность сети не мешает пользоваться
/// встроенными форматами, поэтому приложение обязано запускаться в любом случае.
/// </summary>
public static class NetworkProbe
{
    /// <summary>Короткий таймаут: это индикатор, а не загрузка.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(6);

    public enum State
    {
        Checking,
        Online,
        Offline,
    }

    /// <param name="State">Результат проверки.</param>
    /// <param name="Host">Какой узел проверялся.</param>
    /// <param name="Detail">Пояснение для диагностики.</param>
    public sealed record Result(State State, string? Host, string? Detail);

    /// <summary>
    /// Проверяет доступность узлов из каталога пакетов.
    /// Достаточно, чтобы ответил хотя бы один: площадки разные, и часть может быть
    /// недоступна при работающей остальной сети.
    /// </summary>
    public static async Task<Result> CheckAsync(
        PackageCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        var hosts = catalog.Packages
            .Select(p => p.DownloadUrl)
            .Where(url => !string.IsNullOrEmpty(url))
            .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri is not null)
            .Select(uri => uri!)
            .DistinctBy(uri => uri.Host)
            .ToList();

        if (hosts.Count == 0)
        {
            return new Result(State.Online, null, "The catalog has no external downloads.");
        }

        using var client = new HttpClient { Timeout = ProbeTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NekoConverter/1.0");

        var failures = new List<string>();

        foreach (var host in hosts)
        {
            try
            {
                // HEAD вместо GET: нужен только ответ, а не содержимое.
                using var request = new HttpRequestMessage(HttpMethod.Head, host);
                using var response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                // Любой ответ, включая 403 и 404, означает, что узел достижим.
                // Для индикатора этого достаточно: сеть работает.
                return new Result(State.Online, host.Host, $"HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                failures.Add($"{host.Host}: {ex.GetType().Name}");
            }
        }

        return new Result(
            State.Offline,
            hosts[0].Host,
            failures.Count > 0 ? string.Join("; ", failures.Take(2)) : null);
    }
}
