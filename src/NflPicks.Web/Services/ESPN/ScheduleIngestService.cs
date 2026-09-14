using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NflPicks.Web.Data;
using NflPicks.Web.Services.Espn;
using NflPicks.Web.Data.Entities;

namespace NflPicks.Web.Services.ESPN;

public sealed record IngestReport
{
    public int GamesCreated { get; init; }
    public int GamesUpdated { get; init; }
    public int TeamIdsBackfilled { get; init; }
    public string? TiebreakGame { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public bool Committed { get; init; }
}

/// <summary>
/// Pulls a week's schedule from ESPN into week/game.
///
/// Three rules drive the design:
///
///   1. Idempotent. Keyed on espn_event_id, so re-running is always safe and
///      is the normal way to refresh kickoff times after a flex change.
///
///   2. Never clobbers what you entered. Lines and consensus percentages are
///      yours; scores you marked 'manual' are yours. Ingest fills gaps and
///      updates its own data, nothing else.
///
///   3. Fails loudly on an unknown team abbreviation rather than skipping the
///      game. A silently missing game is a missing pick, and you would not
///      notice until the week was graded.
/// </summary>
public sealed class ScheduleIngestService
{
    private readonly EspnScoreboardClient _espn;
    private readonly IDbContextFactory<PicksDbContext> _dbFactory;
    private readonly ILogger<ScheduleIngestService> _log;

    public ScheduleIngestService(
        EspnScoreboardClient espn,
        IDbContextFactory<PicksDbContext> dbFactory,
        ILogger<ScheduleIngestService> log)
    {
        _espn = espn;
        _dbFactory = dbFactory;
        _log = log;
    }

    public async Task<IngestReport> IngestWeekAsync(
        short seasonYear, short seasonType, short weekNumber, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        using var doc = await _espn.FetchWeekAsync(seasonYear, seasonType, weekNumber, ct);

        if (!doc.RootElement.TryGetProperty("events", out var events)
            || events.ValueKind != JsonValueKind.Array)
        {
            return new IngestReport
            {
                Errors = { "Response had no 'events' array. Check the archived JSON." }
            };
        }

        if (events.GetArrayLength() == 0)
        {
            return new IngestReport
            {
                Errors = { $"ESPN returned zero games for {seasonYear} type {seasonType} " +
                           $"week {weekNumber}. Check the week number." }
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var teamsByAbbr = await db.Teams.ToDictionaryAsync(t => t.EspnAbbr, ct);

        var week = await db.Weeks.FirstOrDefaultAsync(
            w => w.SeasonYear == seasonYear
              && w.SeasonType == seasonType
              && w.WeekNumber == weekNumber, ct);

        if (week is null)
        {
            week = new Week
            {
                SeasonYear = seasonYear,
                SeasonType = seasonType,
                WeekNumber = weekNumber
            };
            db.Weeks.Add(week);
            await db.SaveChangesAsync(ct);
        }

        var existing = await db.Games
            .Where(g => g.WeekId == week.WeekId)
            .ToListAsync(ct);

        int created = 0, updated = 0, backfilled = 0;

        foreach (var ev in events.EnumerateArray())
        {
            var parsed = ParseEvent(ev, teamsByAbbr, errors);
            if (parsed is null) continue;

            backfilled += BackfillEspnTeamIds(parsed, teamsByAbbr);

            var game = existing.FirstOrDefault(g => g.EspnEventId == parsed.EventId)
                    ?? existing.FirstOrDefault(g => g.HomeTeamId == parsed.HomeTeamId
                                                 && g.AwayTeamId == parsed.AwayTeamId);

            if (game is null)
            {
                game = new Game
                {
                    WeekId = week.WeekId,
                    EspnEventId = parsed.EventId,
                    HomeTeamId = parsed.HomeTeamId,
                    AwayTeamId = parsed.AwayTeamId,
                    KickoffUtc = parsed.KickoffUtc,
                    Status = parsed.Status
                };
                db.Games.Add(game);
                existing.Add(game);
                created++;
            }
            else
            {
                game.EspnEventId ??= parsed.EventId;

                if (game.KickoffUtc != parsed.KickoffUtc)
                {
                    warnings.Add(
                        $"{parsed.AwayAbbr} at {parsed.HomeAbbr}: kickoff moved " +
                        $"{game.KickoffUtc:ddd HH:mm} -> {parsed.KickoffUtc:ddd HH:mm} UTC");
                    game.KickoffUtc = parsed.KickoffUtc;
                }

                game.Status = parsed.Status;
                updated++;
            }

            // Scores: only touch what ESPN owns. A manual correction stands.
            if (parsed.HomeScore is not null && game.ScoreSource != "manual")
            {
                game.HomeScore = parsed.HomeScore;
                game.AwayScore = parsed.AwayScore;
                game.ScoreSource = "espn";
                game.ScoresUpdatedAt = DateTime.UtcNow;
            }

            // Lines and percentages are never written here. They are yours.
        }

        if (errors.Count > 0)
        {
            await tx.RollbackAsync(ct);
            return new IngestReport
            {
                Errors = errors,
                Warnings = warnings,
                Committed = false
            };
        }

        await db.SaveChangesAsync(ct);

        var tiebreak = await SetTiebreakGameAsync(db, week, warnings, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new IngestReport
        {
            GamesCreated = created,
            GamesUpdated = updated,
            TeamIdsBackfilled = backfilled,
            TiebreakGame = tiebreak,
            Warnings = warnings,
            Errors = errors,
            Committed = true
        };
    }

    /// <summary>
    /// The tiebreaker is the Monday night game. Only set when currently null --
    /// if you have already pointed the week at a game, ingest leaves it alone.
    /// </summary>
    private static async Task<string?> SetTiebreakGameAsync(
        PicksDbContext db, Week week, List<string> warnings, CancellationToken ct)
    {
        if (week.TiebreakGameId is not null) return "(already set)";

        var et = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var mondayGames = await db.Games
            .Where(g => g.WeekId == week.WeekId)
            .OrderBy(g => g.KickoffUtc)
            .ToListAsync(ct);

        var monday = mondayGames
            .Where(g => TimeZoneInfo
                .ConvertTimeFromUtc(DateTime.SpecifyKind(g.KickoffUtc, DateTimeKind.Utc), et)
                .DayOfWeek == DayOfWeek.Monday)
            .ToList();

        if (monday.Count == 0)
        {
            warnings.Add("No Monday game found; set week.tiebreak_game_id by hand.");
            return null;
        }

        if (monday.Count > 1)
        {
            warnings.Add(
                $"{monday.Count} Monday games this week; picked the latest kickoff. " +
                "Change it by hand if your pool uses the other one.");
        }

        var chosen = monday.OrderByDescending(g => g.KickoffUtc).First();
        week.TiebreakGameId = chosen.GameId;

        return $"game {chosen.GameId}, kickoff {chosen.KickoffUtc:ddd HH:mm} UTC";
    }

    private static int BackfillEspnTeamIds(ParsedEvent ev, Dictionary<string, Team> teams)
    {
        var n = 0;

        if (teams.TryGetValue(ev.HomeAbbr, out var home)
            && home.EspnTeamId is null && ev.HomeEspnId is not null)
        {
            home.EspnTeamId = ev.HomeEspnId;
            n++;
        }

        if (teams.TryGetValue(ev.AwayAbbr, out var away)
            && away.EspnTeamId is null && ev.AwayEspnId is not null)
        {
            away.EspnTeamId = ev.AwayEspnId;
            n++;
        }

        return n;
    }

    private sealed record ParsedEvent(
        long EventId,
        DateTime KickoffUtc,
        string Status,
        string HomeAbbr, short HomeTeamId, int? HomeEspnId, short? HomeScore,
        string AwayAbbr, short AwayTeamId, int? AwayEspnId, short? AwayScore);

    private static ParsedEvent? ParseEvent(
        JsonElement ev, Dictionary<string, Team> teams, List<string> errors)
    {
        try
        {
            var eventId = long.Parse(ev.GetProperty("id").GetString()!);

            var comp = ev.GetProperty("competitions")[0];
            var kickoff = comp.GetProperty("date").GetDateTimeOffset().UtcDateTime;

            var statusType = comp.GetProperty("status").GetProperty("type");
            var state = statusType.GetProperty("state").GetString();
            var name = statusType.TryGetProperty("name", out var n) ? n.GetString() : null;

            var status = name switch
            {
                "STATUS_CANCELED" => "canceled",
                "STATUS_POSTPONED" => "postponed",
                _ => state switch
                {
                    "pre" => "scheduled",
                    "in" => "in_progress",
                    "post" => "final",
                    _ => "scheduled"
                }
            };

            JsonElement? homeEl = null, awayEl = null;

            foreach (var c in comp.GetProperty("competitors").EnumerateArray())
            {
                var side = c.GetProperty("homeAway").GetString();
                if (side == "home") homeEl = c;
                else if (side == "away") awayEl = c;
            }

            if (homeEl is null || awayEl is null)
            {
                errors.Add($"Event {eventId}: could not find both home and away competitors.");
                return null;
            }

            var (homeAbbr, homeEspnId, homeScore) = ReadCompetitor(homeEl.Value);
            var (awayAbbr, awayEspnId, awayScore) = ReadCompetitor(awayEl.Value);

            if (!teams.TryGetValue(homeAbbr, out var homeTeam))
            {
                errors.Add($"Unknown team abbreviation '{homeAbbr}' (event {eventId}). " +
                           "Add it to the team table or fix the mapping.");
                return null;
            }

            if (!teams.TryGetValue(awayAbbr, out var awayTeam))
            {
                errors.Add($"Unknown team abbreviation '{awayAbbr}' (event {eventId}). " +
                           "Add it to the team table or fix the mapping.");
                return null;
            }

            // Only trust scores once the game is actually over.
            if (status != "final")
            {
                homeScore = null;
                awayScore = null;
            }

            return new ParsedEvent(
                eventId, kickoff, status,
                homeAbbr, homeTeam.TeamId, homeEspnId, homeScore,
                awayAbbr, awayTeam.TeamId, awayEspnId, awayScore);
        }
        catch (Exception ex)
        {
            errors.Add($"Could not parse an event: {ex.Message}. See the archived JSON.");
            return null;
        }
    }

    private static (string Abbr, int? EspnId, short? Score) ReadCompetitor(JsonElement c)
    {
        var team = c.GetProperty("team");
        var abbr = team.GetProperty("abbreviation").GetString()!;

        int? espnId = null;
        if (team.TryGetProperty("id", out var idEl)
            && int.TryParse(idEl.GetString(), out var parsedId))
        {
            espnId = parsedId;
        }

        short? score = null;
        if (c.TryGetProperty("score", out var scoreEl))
        {
            var raw = scoreEl.ValueKind == JsonValueKind.String
                ? scoreEl.GetString()
                : scoreEl.GetRawText();

            if (short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                score = s;
        }

        return (abbr, espnId, score);
    }
}
