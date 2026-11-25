using Microsoft.EntityFrameworkCore;
using NflPicks.Web.Data;

namespace NflPicks.Web.Services;

/// <summary>
/// Works out which week a page should open on, and which Sunday a week falls
/// on, from what is already in the database, so neither has to be typed.
/// </summary>
public static class WeekDefaults
{
    public sealed record WeekKey(short SeasonYear, short SeasonType, short WeekNumber);

    private static readonly TimeZoneInfo Et =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private const int RegularSeasonWeeks = 18;

    /// <summary>The most recent week that has been set up, or null if none.</summary>
    public static async Task<WeekKey?> LatestAsync(PicksDbContext db) =>
        await db.Weeks
            .OrderByDescending(w => w.SeasonYear)
            .ThenByDescending(w => w.SeasonType)
            .ThenByDescending(w => w.WeekNumber)
            .Select(w => new WeekKey(w.SeasonYear, w.SeasonType, w.WeekNumber))
            .FirstOrDefaultAsync();

    /// <summary>
    /// The week setup should open on: the latest week while any of its games
    /// still lack a final score (Sunday night, MNF), otherwise the one after it
    /// (Tuesday, when the new board goes up).
    /// </summary>
    public static async Task<WeekKey?> ForSetupAsync(PicksDbContext db)
    {
        var latest = await LatestAsync(db);
        if (latest is null) return null;

        var unfinished = await db.Games.AnyAsync(g =>
            g.Week.SeasonYear == latest.SeasonYear
            && g.Week.SeasonType == latest.SeasonType
            && g.Week.WeekNumber == latest.WeekNumber
            && g.Status != "final");

        var hasGames = await db.Games.AnyAsync(g =>
            g.Week.SeasonYear == latest.SeasonYear
            && g.Week.SeasonType == latest.SeasonType
            && g.Week.WeekNumber == latest.WeekNumber);

        if (unfinished || !hasGames) return latest;

        return latest.SeasonType == 2 && latest.WeekNumber >= RegularSeasonWeeks
            ? new WeekKey(latest.SeasonYear, 3, 1)
            : latest with { WeekNumber = (short)(latest.WeekNumber + 1) };
    }

    /// <summary>
    /// The Sunday a week is anchored on. Taken from the week's own kickoffs if
    /// it has games; otherwise projected from the nearest week in the same
    /// season that does, seven days per week of distance. Null when the season
    /// has no games at all to project from.
    /// </summary>
    public static async Task<DateTime?> SundayForAsync(PicksDbContext db, WeekKey key)
    {
        var kickoffs = await db.Games
            .Where(g => g.Week.SeasonYear == key.SeasonYear
                     && (g.Week.SeasonType == 2 || g.Week.SeasonType == 3))
            .Select(g => new { g.Week.SeasonType, g.Week.WeekNumber, g.KickoffUtc })
            .ToListAsync();

        if (kickoffs.Count == 0 || key.SeasonType is not (2 or 3)) return null;

        var target = Ordinal(key.SeasonType, key.WeekNumber);

        var nearest = kickoffs
            .GroupBy(k => Ordinal(k.SeasonType, k.WeekNumber))
            .OrderBy(grp => Math.Abs(grp.Key - target))
            .First();

        var sunday = SundayOf(nearest.Select(k => k.KickoffUtc));
        return sunday.AddDays(7 * (target - nearest.Key));
    }

    /// <summary>
    /// The Sunday among a week's kickoffs (ET), or the first Sunday after the
    /// earliest one if the week somehow has no Sunday game.
    /// </summary>
    public static DateTime SundayOf(IEnumerable<DateTime> kickoffsUtc)
    {
        var etDates = kickoffsUtc
            .Select(k => TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(k, DateTimeKind.Utc), Et).Date)
            .ToList();

        var sunday = etDates.FirstOrDefault(d => d.DayOfWeek == DayOfWeek.Sunday);
        if (sunday != default) return sunday;

        sunday = etDates.Min();
        while (sunday.DayOfWeek != DayOfWeek.Sunday) sunday = sunday.AddDays(1);
        return sunday;
    }

    // Postseason week 1 follows regular-season week 18, so both count on one line.
    private static int Ordinal(short seasonType, short weekNumber) =>
        seasonType == 3 ? RegularSeasonWeeks + weekNumber : weekNumber;
}
