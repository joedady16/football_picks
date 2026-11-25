-- Migration 002: team form
--
--   docker exec -i nflpicks-db psql -U picks -d nflpicks -v ON_ERROR_STOP=1 \
--       < db/migrations/02_team_form.sql
--
-- Then copy to db/init/04_team_form.sql so a fresh database matches.
-- Safe to re-run.

BEGIN;

-- ---------------------------------------------------------------
-- v_team_game: one row per team per game, so a team's own games can be
-- aggregated without caring which side of the fixture it was on.
--
-- Everything is from that team's perspective: line is what THEY laid or got,
-- cover_margin is how much they beat the number by. Positive covers.
--
-- Note this covers every game in the database, not only ones you picked, so
-- it accumulates 16 games a week rather than however many you got right.
-- ---------------------------------------------------------------
CREATE OR REPLACE VIEW v_team_game AS
SELECT
    w.season_year, w.season_type, w.week_number,
    g.game_id, g.kickoff_utc, g.status,
    g.home_team_id AS team_id,
    g.away_team_id AS opponent_id,
    true AS is_home,
    g.home_score AS points_for,
    g.away_score AS points_against,
    g.grading_line_home AS line,
    (g.home_score - g.away_score) + g.grading_line_home AS cover_margin
FROM game g
JOIN week w ON w.week_id = g.week_id
UNION ALL
SELECT
    w.season_year, w.season_type, w.week_number,
    g.game_id, g.kickoff_utc, g.status,
    g.away_team_id,
    g.home_team_id,
    false,
    g.away_score,
    g.home_score,
    -g.grading_line_home,
    (g.away_score - g.home_score) - g.grading_line_home
FROM game g
JOIN week w ON w.week_id = g.week_id;

-- ---------------------------------------------------------------
-- v_team_form: season aggregates per team.
--
-- ppg / papg / avg_total are the numbers with a real use: they are the
-- starting point for the Monday night total-points tiebreaker, which with 90
-- entrants decides more weeks than any single pick does.
--
-- ats_w/l/p is included because it is the stat everyone quotes, NOT because
-- it predicts anything. Six games is noise, and the line already prices
-- whatever a team has been doing. Read it as history, not as a signal.
--
-- avg_cover_margin is the one with some content: not whether a team covers
-- but by how much it beats or misses the number. Still noisy this early.
-- ---------------------------------------------------------------
CREATE OR REPLACE VIEW v_team_form AS
WITH played AS (
    SELECT * FROM v_team_game
    WHERE status = 'final' AND line IS NOT NULL
)
SELECT
    p.season_year,
    t.team_id,
    t.espn_abbr AS team,
    t.conference,
    t.division,
    count(*) AS games,
    round(avg(p.points_for), 1)                        AS ppg,
    round(avg(p.points_against), 1)                    AS papg,
    round(avg(p.points_for - p.points_against), 1)     AS margin,
    round(avg(p.points_for + p.points_against), 1)     AS avg_total,
    round(avg(p.points_for) FILTER (WHERE p.is_home), 1)       AS ppg_home,
    round(avg(p.points_for) FILTER (WHERE NOT p.is_home), 1)   AS ppg_away,
    count(*) FILTER (WHERE p.cover_margin > 0)         AS ats_w,
    count(*) FILTER (WHERE p.cover_margin < 0)         AS ats_l,
    count(*) FILTER (WHERE p.cover_margin = 0)         AS ats_p,
    round(avg(p.cover_margin), 1)                      AS avg_cover_margin,
    round(avg(p.line), 1)                              AS avg_line
FROM played p
JOIN team t ON t.team_id = p.team_id
GROUP BY p.season_year, t.team_id, t.espn_abbr, t.conference, t.division;

-- ---------------------------------------------------------------
-- v_my_team_record: how you have done picking each team.
--
-- This one is about you rather than about the team, which makes it more
-- interesting than the ATS column — a blind spot for a particular team is a
-- fact about your judgment and is at least potentially correctable.
-- Sample sizes will be tiny (a handful of picks per team per season), so
-- treat it as a curiosity until several seasons have stacked up.
-- ---------------------------------------------------------------
CREATE OR REPLACE VIEW v_my_team_record AS
SELECT
    season_year,
    picked_team AS team,
    count(*) FILTER (WHERE result IN ('win', 'loss')) AS graded,
    count(*) FILTER (WHERE result = 'win')            AS wins,
    count(*) FILTER (WHERE result = 'loss')           AS losses
FROM v_pick_result
WHERE is_me
GROUP BY season_year, picked_team;

INSERT INTO schema_migration (filename)
VALUES ('02_team_form.sql')
ON CONFLICT (filename) DO NOTHING;

COMMIT;
