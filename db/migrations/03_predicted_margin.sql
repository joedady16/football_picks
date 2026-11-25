-- Migration 003: predicted margin
--
--   docker exec -i nflpicks-db psql -U picks -d nflpicks -v ON_ERROR_STOP=1 \
--       < db/migrations/03_predicted_margin.sql
--
-- Then copy to db/init/05_predicted_margin.sql so a fresh database matches.
-- Safe to re-run.

BEGIN;

-- ---------------------------------------------------------------
-- pick.predicted_margin_home: what you thought the game would finish at,
-- recorded before kickoff. Home perspective like every other number in the
-- schema: +7 means you had the home team winning by 7.
--
-- The point is "edge": how far your number sits from CBS's line on the side
-- you took. Edge 0.5 means you barely liked it, edge 7 means you thought the
-- line was badly wrong. Whether big-edge picks actually win more is the
-- question this column exists to answer.
-- ---------------------------------------------------------------
ALTER TABLE pick ADD COLUMN IF NOT EXISTS predicted_margin_home NUMERIC(4,1);

-- ---------------------------------------------------------------
-- v_pick_edge: one row per pick that has a prediction.
--
--   predicted_margin_for_pick  your number from the picked team's side
--   edge                       predicted_margin_for_pick + line_for_pick
--
-- Positive edge = your prediction says the pick covers. Zero or negative
-- means your own number disagreed with your pick, worth noticing in itself.
-- ---------------------------------------------------------------
CREATE OR REPLACE VIEW v_pick_edge AS
SELECT
    r.pick_id, r.entrant_id, r.is_me, r.season_year, r.season_type, r.week_number,
    r.game_id, r.away_team, r.home_team, r.picked_team, r.line_for_pick,
    p.predicted_margin_home,
    CASE WHEN r.picked_home THEN p.predicted_margin_home
         ELSE -p.predicted_margin_home END AS predicted_margin_for_pick,
    CASE WHEN r.picked_home THEN p.predicted_margin_home
         ELSE -p.predicted_margin_home END + r.line_for_pick AS edge,
    r.cover_margin, r.result
FROM v_pick_result r
JOIN pick p ON p.pick_id = r.pick_id
WHERE p.predicted_margin_home IS NOT NULL;

-- ---------------------------------------------------------------
-- v_my_splits: same as 001 plus an 'edge' dimension. Output columns are
-- unchanged, so /season picks it up without code changes.
-- ---------------------------------------------------------------
CREATE OR REPLACE VIEW v_my_splits AS
WITH graded AS (
    SELECT * FROM v_pick_result
    WHERE is_me AND result IN ('win', 'loss')
)
SELECT season_year, 'favorite/dog' AS dimension, pick_side AS bucket,
       count(*) AS graded, count(*) FILTER (WHERE result = 'win') AS wins,
       round(count(*) FILTER (WHERE result = 'win')::numeric / count(*) * 100, 1) AS win_pct
FROM graded GROUP BY season_year, pick_side
UNION ALL
SELECT season_year, 'home/away',
       CASE WHEN picked_home THEN 'picked home' ELSE 'picked away' END,
       count(*), count(*) FILTER (WHERE result = 'win'),
       round(count(*) FILTER (WHERE result = 'win')::numeric / count(*) * 100, 1)
FROM graded GROUP BY season_year, 2, 3
UNION ALL
SELECT season_year, 'line size', line_bucket,
       count(*), count(*) FILTER (WHERE result = 'win'),
       round(count(*) FILTER (WHERE result = 'win')::numeric / count(*) * 100, 1)
FROM graded GROUP BY season_year, line_bucket
UNION ALL
SELECT season_year, 'primetime',
       CASE WHEN is_primetime THEN 'primetime' ELSE 'daytime' END,
       count(*), count(*) FILTER (WHERE result = 'win'),
       round(count(*) FILTER (WHERE result = 'win')::numeric / count(*) * 100, 1)
FROM graded GROUP BY season_year, 2, 3
UNION ALL
SELECT season_year, 'divisional',
       CASE WHEN is_divisional THEN 'divisional' ELSE 'non-divisional' END,
       count(*), count(*) FILTER (WHERE result = 'win'),
       round(count(*) FILTER (WHERE result = 'win')::numeric / count(*) * 100, 1)
FROM graded GROUP BY season_year, 2, 3
UNION ALL
SELECT season_year, 'day', kickoff_day_et,
       count(*), count(*) FILTER (WHERE result = 'win'),
       round(count(*) FILTER (WHERE result = 'win')::numeric / count(*) * 100, 1)
FROM graded GROUP BY season_year, kickoff_day_et
UNION ALL
SELECT season_year, 'vs public',
       CASE WHEN public_pct_on_my_side >= 70 THEN 'with public 70+'
            WHEN public_pct_on_my_side >= 55 THEN 'lean with 55-69'
            WHEN public_pct_on_my_side >  45 THEN 'split 46-54'
            WHEN public_pct_on_my_side >= 30 THEN 'lean against 30-45'
            ELSE 'against public under 30' END,
       count(*), count(*) FILTER (WHERE result = 'win'),
       round(count(*) FILTER (WHERE result = 'win')::numeric / count(*) * 100, 1)
FROM graded WHERE public_pct_on_my_side IS NOT NULL GROUP BY season_year, 2, 3
UNION ALL
SELECT season_year, 'changed pick',
       CASE WHEN was_changed THEN 'changed' ELSE 'first instinct' END,
       count(*), count(*) FILTER (WHERE result = 'win'),
       round(count(*) FILTER (WHERE result = 'win')::numeric / count(*) * 100, 1)
FROM graded GROUP BY season_year, 2, 3
UNION ALL
SELECT season_year, 'edge',
       CASE WHEN edge <= 0 THEN 'my number disagreed'
            WHEN edge <= 3 THEN 'edge 0.5-3'
            WHEN edge <= 7 THEN 'edge 3.5-7'
            ELSE 'edge 7.5+' END,
       count(*), count(*) FILTER (WHERE result = 'win'),
       round(count(*) FILTER (WHERE result = 'win')::numeric / count(*) * 100, 1)
FROM v_pick_edge
WHERE is_me AND result IN ('win', 'loss')
GROUP BY season_year, 2, 3;

INSERT INTO schema_migration (filename)
VALUES ('03_predicted_margin.sql')
ON CONFLICT (filename) DO NOTHING;

COMMIT;
