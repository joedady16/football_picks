using System.Text.Json;

namespace NflPicks.Web.Services.Espn;

/// <summary>
/// Thin wrapper over ESPN's undocumented scoreboard endpoint.
///
/// Every response is written to disk before it is parsed. The endpoint is
/// unversioned and can change without notice, so when a pull fails the raw
/// payload is the thing you need -- reproducing it later after the games have
/// moved on is not always possible.
/// </summary>
public sealed class EspnScoreboardClient
{
    private const string BaseUrl =
        "https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard";

    private readonly HttpClient _http;
    private readonly ILogger<EspnScoreboardClient> _log;
    private readonly string _archiveDir;

    public EspnScoreboardClient(
        HttpClient http,
        ILogger<EspnScoreboardClient> log,
        IWebHostEnvironment env)
    {
        _http = http;
        _log = log;
        _archiveDir = Path.Combine(env.ContentRootPath, "espn-archive");
    }

    /// <param name="seasonYear">e.g. 2026</param>
    /// <param name="seasonType">1 = pre, 2 = regular, 3 = post</param>
    /// <param name="weekNumber">week within that season type</param>
    public async Task<JsonDocument> FetchWeekAsync(
        int seasonYear, int seasonType, int weekNumber, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?dates={seasonYear}&seasontype={seasonType}&week={weekNumber}";

        _log.LogInformation("ESPN fetch {Url}", url);

        using var response = await _http.GetAsync(url, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        await ArchiveAsync(seasonYear, seasonType, weekNumber, json, ct);

        // Archive first, then throw -- an error body is often the most
        // informative thing you'll get.
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(json);
    }

    private async Task ArchiveAsync(
        int seasonYear, int seasonType, int weekNumber, string json, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_archiveDir);

            var name = $"{seasonYear}-t{seasonType}-w{weekNumber:00}-" +
                       $"{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";

            await File.WriteAllTextAsync(Path.Combine(_archiveDir, name), json, ct);
        }
        catch (Exception ex)
        {
            // Archiving is a convenience, not a precondition. Never let it
            // take down an otherwise good ingest.
            _log.LogWarning(ex, "Could not archive ESPN response");
        }
    }
}
