using Npgsql;

namespace NflPicks.Web.Services;

/// <summary>
/// Read-only queries behind the team pages. Raw SQL over v_team_game, like the
/// other analysis pages, because the window functions read better in SQL.
/// </summary>
public sealed class TeamStats(NpgsqlDataSource dataSource)
{
    public sealed record TeamSummary(
        short TeamId, string Abbr, string FullName, string Conference, string Division,
        int Games, int Wins, int Losses, int Ties,
        decimal? Ppg, decimal? Papg, decimal? Margin, decimal? Last5Margin, string Last5,
        decimal? AvgTotal,
        int HomeW, int HomeL, int AwayW, int AwayL,
        int AtsW, int AtsL, int AtsP, decimal? AvgCover)
    {
        public string Record => Ties > 0 ? $"{Wins}-{Losses}-{Ties}" : $"{Wins}-{Losses}";
        public string Ats => AtsP > 0 ? $"{AtsW}-{AtsL}-{AtsP}" : $"{AtsW}-{AtsL}";
    }

    /// <summary>One game from a single team's point of view.</summary>
    public sealed record TeamGame(
        int GameId, short SeasonType, short Week, DateTime KickoffUtc, string Status,
        bool IsHome, string Opponent,
        short? PointsFor, short? PointsAgainst,
        decimal? Line, decimal? CoverMargin, decimal? PublicPctOnTeam,
        string? MyPick)
    {
        public bool IsFinal => Status == "final" && PointsFor is not null && PointsAgainst is not null;

        public string? Outcome => !IsFinal ? null
            : PointsFor > PointsAgainst ? "W" : PointsFor < PointsAgainst ? "L" : "T";

        /// <summary>win/loss/push for your pick in this game, whichever side it was on.</summary>
        public string? MyResult
        {
            get
            {
                if (!IsFinal || MyPick is null || CoverMargin is null) return null;
                var margin = MyPick == Opponent ? -CoverMargin.Value : CoverMargin.Value;
                return margin > 0 ? "win" : margin < 0 ? "loss" : "push";
            }
        }
    }

    public async Task<short?> LatestSeasonAsync()
    {
        await using var cmd = dataSource.CreateCommand("SELECT max(season_year) FROM week");
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull or null ? null : Convert.ToInt16(v);
    }

    public async Task<List<TeamSummary>> SummariesAsync(short season)
    {
        const string sql = """
            WITH g AS (
                SELECT v.*,
                       row_number() OVER (PARTITION BY v.team_id ORDER BY v.kickoff_utc DESC) AS rn
                FROM v_team_game v
                WHERE v.season_year = @y AND v.status = 'final'
            )
            SELECT t.team_id, t.espn_abbr, t.full_name, t.conference, t.division,
                   count(g.game_id),
                   count(*) FILTER (WHERE g.points_for >  g.points_against),
                   count(*) FILTER (WHERE g.points_for <  g.points_against),
                   count(*) FILTER (WHERE g.points_for =  g.points_against),
                   round(avg(g.points_for), 1),
                   round(avg(g.points_against), 1),
                   round(avg(g.points_for - g.points_against), 1),
                   round(avg(g.points_for - g.points_against) FILTER (WHERE g.rn <= 5), 1),
                   coalesce(string_agg(CASE WHEN g.points_for > g.points_against THEN 'W'
                                            WHEN g.points_for < g.points_against THEN 'L'
                                            ELSE 'T' END, '' ORDER BY g.kickoff_utc)
                            FILTER (WHERE g.rn <= 5), ''),
                   round(avg(g.points_for + g.points_against), 1),
                   count(*) FILTER (WHERE g.is_home     AND g.points_for > g.points_against),
                   count(*) FILTER (WHERE g.is_home     AND g.points_for < g.points_against),
                   count(*) FILTER (WHERE NOT g.is_home AND g.points_for > g.points_against),
                   count(*) FILTER (WHERE NOT g.is_home AND g.points_for < g.points_against),
                   count(*) FILTER (WHERE g.cover_margin > 0),
                   count(*) FILTER (WHERE g.cover_margin < 0),
                   count(*) FILTER (WHERE g.cover_margin = 0),
                   round(avg(g.cover_margin), 1)
            FROM team t
            LEFT JOIN g ON g.team_id = t.team_id
            GROUP BY t.team_id, t.espn_abbr, t.full_name, t.conference, t.division
            ORDER BY t.conference, t.division, t.espn_abbr
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("y", season);
        await using var rd = await cmd.ExecuteReaderAsync();

        var list = new List<TeamSummary>();
        while (await rd.ReadAsync())
        {
            decimal? Dec(int i) => rd.IsDBNull(i) ? null : rd.GetDecimal(i);
            int Cnt(int i) => (int)rd.GetInt64(i);

            list.Add(new TeamSummary(
                rd.GetInt16(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4),
                Cnt(5), Cnt(6), Cnt(7), Cnt(8),
                Dec(9), Dec(10), Dec(11), Dec(12), rd.GetString(13),
                Dec(14),
                Cnt(15), Cnt(16), Cnt(17), Cnt(18),
                Cnt(19), Cnt(20), Cnt(21), Dec(22)));
        }
        return list;
    }

    /// <summary>Every game this season for one team, played or not, oldest first.</summary>
    public async Task<List<TeamGame>> GamesAsync(short season, short teamId)
    {
        const string sql = """
            SELECT v.game_id, v.season_type, v.week_number, v.kickoff_utc, v.status,
                   v.is_home, o.espn_abbr,
                   v.points_for, v.points_against, v.line, v.cover_margin,
                   CASE WHEN v.is_home THEN g.public_pct_home ELSE 100 - g.public_pct_home END,
                   pt.espn_abbr
            FROM v_team_game v
            JOIN team o  ON o.team_id = v.opponent_id
            JOIN game g  ON g.game_id = v.game_id
            LEFT JOIN pick p ON p.game_id = v.game_id
                            AND p.entrant_id = (SELECT entrant_id FROM entrant WHERE is_me)
            LEFT JOIN team pt ON pt.team_id = p.picked_team_id
            WHERE v.season_year = @y AND v.team_id = @t
            ORDER BY v.kickoff_utc
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("y", season);
        cmd.Parameters.AddWithValue("t", teamId);
        await using var rd = await cmd.ExecuteReaderAsync();

        var list = new List<TeamGame>();
        while (await rd.ReadAsync())
        {
            list.Add(new TeamGame(
                rd.GetInt32(0), rd.GetInt16(1), rd.GetInt16(2),
                DateTime.SpecifyKind(rd.GetDateTime(3), DateTimeKind.Utc),
                rd.GetString(4), rd.GetBoolean(5), rd.GetString(6),
                rd.IsDBNull(7) ? null : rd.GetInt16(7),
                rd.IsDBNull(8) ? null : rd.GetInt16(8),
                rd.IsDBNull(9) ? null : rd.GetDecimal(9),
                rd.IsDBNull(10) ? null : rd.GetDecimal(10),
                rd.IsDBNull(11) ? null : rd.GetDecimal(11),
                rd.IsDBNull(12) ? null : rd.GetString(12)));
        }
        return list;
    }
}
