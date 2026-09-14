-- Migration 001: pool standings context
--
-- Apply to a live database (no data loss):
--   docker exec -i nflpicks-db psql -U picks -d nflpicks -v ON_ERROR_STOP=1 \
--       < db/migrations/001_pool_standings.sql
--
-- Safe to re-run. Every statement is idempotent, and the ledger at the bottom
-- records that it ran so you can tell what a given database has had applied.
--
-- 01_schema.sql has been updated to match, so a fresh `down -v` produces the
-- same shape. Those two must never drift: the init script builds new
-- databases, migrations bring existing ones forward, and they have to agree.

BEGIN;

CREATE TABLE IF NOT EXISTS schema_migration (
    filename    TEXT        PRIMARY KEY,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------
-- Pool context, read off the CBS standings page each week.
--
-- Your own record cannot tell you whether you are inside the cut. Three
-- numbers a week can: what the leader has, what the cut line has, and which
-- rank you are calling the cut. Storing cutoff_rank matters because "top 10%"
-- of a pool that drifts between 85 and 95 people is not a fixed position, and
-- a gap measured against a moving target you did not record is unreadable
-- later.
-- ---------------------------------------------------------------
ALTER TABLE week
    ADD COLUMN IF NOT EXISTS cutoff_rank         SMALLINT,
    ADD COLUMN IF NOT EXISTS cutoff_season_wins  SMALLINT,
    ADD COLUMN IF NOT EXISTS leader_season_wins  SMALLINT;

COMMENT ON COLUMN week.cutoff_rank IS
    'Rank you are treating as the prize cut, e.g. 9 for top 10% of ~90.';
COMMENT ON COLUMN week.cutoff_season_wins IS
    'Season-to-date wins of the entrant sitting at cutoff_rank, as of this week.';
COMMENT ON COLUMN week.leader_season_wins IS
    'Season-to-date wins of the pool leader, as of this week.';

-- entrant_week already holds per-week reported numbers. These are the
-- season-to-date pair, kept separate rather than overloading the weekly ones.
ALTER TABLE entrant_week
    ADD COLUMN IF NOT EXISTS cbs_season_wins  SMALLINT,
    ADD COLUMN IF NOT EXISTS cbs_season_rank  SMALLINT;

COMMENT ON COLUMN entrant_week.cbs_season_wins IS
    'Season-to-date wins CBS reports for this entrant as of this week.';

-- ---------------------------------------------------------------
-- v_season_standing: the "where do I stand" view.
--
-- season_wins is computed from your own picks. cbs_season_wins is what CBS
-- says. The reconcile column flags any disagreement, which is almost always a
-- data entry error on your side and is much cheaper to find in week 4 than in
-- January.
--
-- gap_to_cutoff is the number that answers your actual question. Positive means
-- you are inside the cut with room; negative is how many games you are chasing.
-- ---------------------------------------------------------------
CREATE OR REPLACE VIEW v_season_standing AS
WITH my_week AS (
    SELECT season_year, season_type, week_number,
           count(*) FILTER (WHERE result = 'win')  AS wins,
           count(*) FILTER (WHERE result = 'loss') AS losses,
           count(*) FILTER (WHERE result = 'push') AS pushes
    FROM v_pick_result
    WHERE is_me
    GROUP BY season_year, season_type, week_number
),
running AS (
    SELECT m.*,
           sum(m.wins) OVER (PARTITION BY m.season_year, m.season_type
                             ORDER BY m.week_number) AS season_wins,
           sum(m.losses) OVER (PARTITION BY m.season_year, m.season_type
                               ORDER BY m.week_number) AS season_losses
    FROM my_week m
)
SELECT
    r.season_year,
    r.season_type,
    r.week_number,
    r.wins,
    r.losses,
    r.pushes,
    r.season_wins,
    r.season_losses,
    round(r.season_wins::numeric
          / NULLIF(r.season_wins + r.season_losses, 0) * 100, 1) AS season_pct,
    w.pool_size,
    ew.cbs_season_wins,
    ew.cbs_season_rank,
    w.leader_season_wins,
    w.cutoff_rank,
    w.cutoff_season_wins,
    r.season_wins - w.cutoff_season_wins AS gap_to_cutoff,
    r.season_wins - w.leader_season_wins AS gap_to_leader,
    CASE
        WHEN ew.cbs_season_wins IS NOT NULL
         AND ew.cbs_season_wins <> r.season_wins
        THEN 'MISMATCH'
    END AS reconcile
FROM running r
JOIN week w
     ON w.season_year = r.season_year
    AND w.season_type = r.season_type
    AND w.week_number = r.week_number
LEFT JOIN entrant e ON e.is_me
LEFT JOIN entrant_week ew
     ON ew.week_id = w.week_id AND ew.entrant_id = e.entrant_id;

-- ---------------------------------------------------------------
-- v_my_splits: your record cut by each analysis dimension.
--
-- Read the 'graded' column before the percentage, always. Most buckets will
-- hold 10-40 picks for a while, and at that size the win rates move several
-- points on a single game. These are a record of what happened, not a signal
-- to act on, until the counts get large.
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
FROM graded GROUP BY season_year, 2, 3;

INSERT INTO schema_migration (filename)
VALUES ('001_pool_standings.sql')
ON CONFLICT (filename) DO NOTHING;

COMMIT;
