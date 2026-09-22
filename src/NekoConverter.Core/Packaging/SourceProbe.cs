using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace NekoConverter.Core.Packaging;

/// <param name="Url">Адрес.</param>
/// <param name="Host">Узел — для показа человеку.</param>
/// <param name="Latency">Время до первого байта. null — узел недоступен.</param>
/// <param name="Error">Причина недоступности.</param>
public sealed record ProbedSource(string Url, string Host, TimeSpan? Latency, string? Error)
{
    public bool Reachable => Latency is not null;
}

/// <summary>
/// Выбор быстрейшего источника загрузки.
///
/// Зачем измерять, а не полагаться на список: в сетях с нестабильным доступом
/// к зарубежным узлам один и тот же адрес сегодня отвечает за доли секунды,
/// а завтра висит минутами. Жёстко прописанный «быстрый» источник в такой
/// ситуации только вредит.
///
/// Поэтому все адреса проверяются ОДНОВРЕМЕННО, и первым используется тот,
/// который реально ответил быстрее всех. Проверка лёгкая: запрашивается один
/// байт, а не весь файл.
/// </summary>
public static class SourceProbe
{
    /// <summary>Сколько ждать ответа от одного узла. Больше незачем: это замер, а не загрузка.</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    public static async Task<IReadOnlyList<ProbedSource>> RankAsync(
        IEnumerable<string> urls,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var distinct = urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
        {
            return [];
        }

        // Один клиент на все замеры: так соединения переиспользуются,
        // и замер отражает реальную стоимость загрузки, а не установку TLS.
        using var client = new HttpClient
        {
            Timeout = timeout ?? DefaultTimeout,
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("NekoConverter/1.0");

        var probes = distinct.Select(url => ProbeAsync(client, url, cancellationToken)).ToList();

        var results = await Task.WhenAll(probes).ConfigureAwait(false);

        // Сначала доступные, среди них — по возрастанию задержки.
        return results
            .OrderBy(r => r.Reachable ? 0 : 1)
            .ThenBy(r => r.Latency ?? TimeSpan.MaxValue)
            .ToList();
    }

    private static async Task<ProbedSource> ProbeAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : url;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Просим ровно один байт: цель — измерить задержку, а не скачать файл.
            request.Headers.Range = new RangeHeaderValue(0, 0);

            var stopwatch = Stopwatch.StartNew();

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            // Даже отказ «диапазон не поддерживается» означает, что узел отвечает:
            // для выбора источника этого достаточно.
            if (response.StatusCode is HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                return new ProbedSource(url, host, stopwatch.Elapsed, null);
            }

            response.EnsureSuccessStatusCode();

            // Читаем первый байт, чтобы замер отражал не только заголовки.
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            var buffer = new byte[1];
            _ = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            return new ProbedSource(url, host, stopwatch.Elapsed, null);
        }
        catch (Exception ex)
        {
            return new ProbedSource(url, host, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
