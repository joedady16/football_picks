-- NFL ATS Pick Tracker - schema
-- Target: PostgreSQL 17
-- Runs automatically via /docker-entrypoint-initdb.d on FIRST container init.
-- After editing this file: docker compose down -v && docker compose up -d
--
-- Pool this is built for:
--   ~90 entrants, ATS picks, one CBS line per game posted Tuesday and never
--   moved, picks editable until each game's own kickoff, one weekly winner,
--   season-long top-10 prizes, MNF total-points tiebreaker.

-- ===============================================================
-- Reference
-- ===============================================================

CREATE TABLE team (
    team_id       SMALLINT    PRIMARY KEY,
    espn_abbr     TEXT        NOT NULL UNIQUE,
    espn_team_id  INTEGER     UNIQUE,
    full_name     TEXT        NOT NULL,
    conference    TEXT        NOT NULL CHECK (conference IN ('AFC', 'NFC')),
    division      TEXT        NOT NULL CHECK (division IN ('East', 'North', 'South', 'West'))
);

-- You, plus whichever leaguemates you choose to store picks for.
-- With ~90 people you are not expected to store everyone: league consensus
-- can come from a sample, and national consensus is free per game.
CREATE TABLE entrant (
    entrant_id    SERIAL   PRIMARY KEY,
    display_name  TEXT     NOT NULL UNIQUE,   -- must match the CBS screen name to import
    is_me         BOOLEAN  NOT NULL DEFAULT false,
    is_active     BOOLEAN  NOT NULL DEFAULT true,
    is_tracked    BOOLEAN  NOT NULL DEFAULT true,  -- false = name known, picks not stored
    note          TEXT
);

CREATE UNIQUE INDEX ux_entrant_is_me ON entrant (is_me) WHERE is_me;

-- ===============================================================
-- Weeks and games
-- ===============================================================

CREATE TABLE week (
    week_id            SERIAL      PRIMARY KEY,
    season_year        SMALLINT    NOT NULL,
    season_type        SMALLINT    NOT NULL DEFAULT 2,   -- 1=pre, 2=regular, 3=post
    week_number        SMALLINT    NOT NULL CHECK (week_number BETWEEN 1 AND 22),
    spreads_posted_at  TIMESTAMPTZ,
    pool_size          SMALLINT,                         -- your league's size that week; it drifts
    tiebreak_game_id   INTEGER,                          -- FK added after game exists
    UNIQUE (season_year, season_type, week_number)
);

-- Lines are ALWAYS from the home team's perspective: -3.5 = home favored by 3.5.
--
-- Two different consensus numbers, deliberately not merged:
--   public_pct_home = CBS's site-wide percentage. Free, every game, but it is
--                     the national user base, not your 90-person league.
--   league_pct_home = your league's share. Enter it if CBS shows a pool grid,
--                     or leave null and let the views estimate from stored picks.
-- Keeping both is what lets you measure whether the free number is a usable
-- stand-in for the one that actually decides your weekly prize.
CREATE TABLE game (
    game_id            SERIAL       PRIMARY KEY,
    week_id            INTEGER      NOT NULL REFERENCES week(week_id) ON DELETE CASCADE,
    espn_event_id      BIGINT       UNIQUE,
    home_team_id       SMALLINT     NOT NULL REFERENCES team(team_id),
    away_team_id       SMALLINT     NOT NULL REFERENCES team(team_id),
    kickoff_utc        TIMESTAMPTZ  NOT NULL,
    grading_line_home  NUMERIC(4,1),
    public_pct_home    NUMERIC(5,2) CHECK (public_pct_home BETWEEN 0 AND 100),
    league_pct_home    NUMERIC(5,2) CHECK (league_pct_home BETWEEN 0 AND 100),
    pct_as_of          TIMESTAMPTZ,
    home_score         SMALLINT,
    away_score         SMALLINT,
    status             TEXT         NOT NULL DEFAULT 'scheduled'
                                    CHECK (status IN ('scheduled','in_progress','final','postponed','canceled')),
    score_source       TEXT         CHECK (score_source IN ('espn','manual')),
    scores_updated_at  TIMESTAMPTZ,
    CONSTRAINT ck_game_distinct_teams CHECK (home_team_id <> away_team_id),
    CONSTRAINT ck_game_final_has_score CHECK (
        status <> 'final' OR (home_score IS NOT NULL AND away_score IS NOT NULL)
    )
);

CREATE INDEX ix_game_week    ON game (week_id);
CREATE INDEX ix_game_kickoff ON game (kickoff_utc);

-- Circular reference, so it lands after both tables exist.
ALTER TABLE week
    ADD CONSTRAINT fk_week_tiebreak_game
    FOREIGN KEY (tiebreak_game_id) REFERENCES game(game_id) ON DELETE SET NULL;

-- ===============================================================
-- Picks
-- ===============================================================

CREATE TABLE pick (
    pick_id         SERIAL       PRIMARY KEY,
    entrant_id      INTEGER      NOT NULL REFERENCES entrant(entrant_id) ON DELETE CASCADE,
    game_id         INTEGER      NOT NULL REFERENCES game(game_id) ON DELETE CASCADE,
    picked_team_id  SMALLINT     NOT NULL REFERENCES team(team_id),
    first_picked_at TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    source          TEXT         NOT NULL DEFAULT 'manual' CHECK (source IN ('manual','import')),
    note            TEXT,
    CONSTRAINT ux_pick_entrant_game UNIQUE (entrant_id, game_id)
);

CREATE INDEX ix_pick_game ON pick (game_id);
CREATE INDEX ix_pick_team ON pick (picked_team_id);

-- You can edit until each kickoff, so every flip is recorded. This answers
-- "do my second thoughts beat my first instinct" -- which needs seasons of
-- data, which is exactly why it collects from week 1 instead of being bolted
-- on later. hours_before_kickoff is stored because it is only knowable now.
CREATE TABLE pick_change (
    change_id             SERIAL       PRIMARY KEY,
    pick_id               INTEGER      NOT NULL REFERENCES pick(pick_id) ON DELETE CASCADE,
    from_team_id          SMALLINT     NOT NULL REFERENCES team(team_id),
    to_team_id            SMALLINT     NOT NULL REFERENCES team(team_id),
    changed_at            TIMESTAMPTZ  NOT NULL DEFAULT now(),
    hours_before_kickoff  NUMERIC(6,2)
);

CREATE INDEX ix_pick_change_pick ON pick_change (pick_id);

-- Per-week, per-entrant extras. cbs_reported_* let you reconcile against the
-- CBS standings page -- if your computed record disagrees with theirs in
-- week 3, your data is wrong and you want to know then, not in January.
CREATE TABLE entrant_week (
    entrant_id         INTEGER   NOT NULL REFERENCES entrant(entrant_id) ON DELETE CASCADE,
    week_id            INTEGER   NOT NULL REFERENCES week(week_id) ON DELETE CASCADE,
    tiebreak_guess     INTEGER,
    cbs_reported_wins  SMALLINT,
    cbs_reported_rank  SMALLINT,
    PRIMARY KEY (entrant_id, week_id)
);

-- ===============================================================
-- Triggers
-- ===============================================================

-- Two invariants a CHECK cannot express.
CREATE OR REPLACE FUNCTION fn_pick_validate() RETURNS TRIGGER AS $$
DECLARE
    g RECORD;
BEGIN
    SELECT home_team_id, away_team_id, grading_line_home
      INTO g
      FROM game WHERE game_id = NEW.game_id;

    IF NEW.picked_team_id NOT IN (g.home_team_id, g.away_team_id) THEN
        RAISE EXCEPTION 'picked_team_id % is not playing in game %',
            NEW.picked_team_id, NEW.game_id;
    END IF;

    IF g.grading_line_home IS NULL THEN
        RAISE EXCEPTION 'game % has no line; set grading_line_home before storing picks',
            NEW.game_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_pick_validate
    BEFORE INSERT OR UPDATE ON pick
    FOR EACH ROW EXECUTE FUNCTION fn_pick_validate();

CREATE OR REPLACE FUNCTION fn_pick_audit() RETURNS TRIGGER AS $$
DECLARE
    ko TIMESTAMPTZ;
BEGIN
    IF NEW.picked_team_id IS DISTINCT FROM OLD.picked_team_id THEN
        SELECT kickoff_utc INTO ko FROM game WHERE game_id = NEW.game_id;

        INSERT INTO pick_change (pick_id, from_team_id, to_team_id, hours_before_kickoff)
        VALUES (NEW.pick_id, OLD.picked_team_id, NEW.picked_team_id,
                round(EXTRACT(EPOCH FROM (ko - now())) / 3600.0, 2));

        NEW.updated_at := now();
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_pick_audit
    BEFORE UPDATE ON pick
    FOR EACH ROW EXECUTE FUNCTION fn_pick_audit();

-- ===============================================================
-- Views
-- ===============================================================

-- Grading, analysis dimensions, and both consensus numbers side by side.
--
-- Cover margin from the perspective of the picked team:
--   picked home -> (home - away) + line
--   picked away -> (away - home) - line
-- Positive = cover, zero = push, negative = loss.
--
-- league_pct_on_my_side prefers the entered league number and falls back to
-- counting stored picks; league_pct_source says which, so you never mistake a
-- 5-person sample for your 90-person field.
CREATE VIEW v_pick_result AS
WITH base AS (
    SELECT
        p.pick_id, p.note, p.first_picked_at, p.updated_at, p.source,
        e.entrant_id, e.display_name AS entrant, e.is_me,
        w.season_year, w.season_type, w.week_number,
        g.game_id, g.kickoff_utc, g.status, g.home_score, g.away_score,
        g.grading_line_home AS line_home,
        g.public_pct_home, g.league_pct_home,
        ht.espn_abbr AS home_team,
        at.espn_abbr AS away_team,
        pt.espn_abbr AS picked_team,
        (p.picked_team_id = g.home_team_id) AS picked_home,
        (ht.conference = at.conference AND ht.division = at.division) AS is_divisional,
        CASE WHEN p.picked_team_id = g.home_team_id
             THEN g.grading_line_home ELSE -g.grading_line_home END AS line_for_pick,
        count(*) OVER (PARTITION BY g.game_id) AS tracked_in_game,
        count(*) FILTER (WHERE p.picked_team_id = g.home_team_id)
            OVER (PARTITION BY g.game_id) AS tracked_on_home,
        EXISTS (SELECT 1 FROM pick_change pc WHERE pc.pick_id = p.pick_id) AS was_changed
    FROM pick p
    JOIN entrant e ON e.entrant_id = p.entrant_id
    JOIN game g    ON g.game_id    = p.game_id
    JOIN week w    ON w.week_id    = g.week_id
    JOIN team ht   ON ht.team_id   = g.home_team_id
    JOIN team at   ON at.team_id   = g.away_team_id
    JOIN team pt   ON pt.team_id   = p.picked_team_id
)
SELECT
    b.*,
    CASE WHEN b.line_for_pick < 0 THEN 'favorite'
         WHEN b.line_for_pick > 0 THEN 'underdog'
         ELSE 'pickem' END AS pick_side,
    CASE WHEN abs(b.line_for_pick) <= 3  THEN '0-3'
         WHEN abs(b.line_for_pick) <= 7  THEN '3.5-7'
         WHEN abs(b.line_for_pick) <= 10 THEN '7.5-10'
         ELSE '10.5+' END AS line_bucket,
    (EXTRACT(HOUR FROM b.kickoff_utc AT TIME ZONE 'America/New_York') >= 19) AS is_primetime,
    to_char(b.kickoff_utc AT TIME ZONE 'America/New_York', 'Dy') AS kickoff_day_et,

    CASE WHEN b.picked_home THEN b.public_pct_home
         ELSE 100 - b.public_pct_home END AS public_pct_on_my_side,

    CASE WHEN b.league_pct_home IS NOT NULL THEN 'entered'
         WHEN b.tracked_in_game > 1        THEN 'sampled'
         ELSE 'none' END AS league_pct_source,

    CASE WHEN b.league_pct_home IS NOT NULL THEN
             CASE WHEN b.picked_home THEN b.league_pct_home ELSE 100 - b.league_pct_home END
         WHEN b.tracked_in_game > 1 THEN
             round((CASE WHEN b.picked_home THEN b.tracked_on_home
                         ELSE b.tracked_in_game - b.tracked_on_home END)::numeric
                   / b.tracked_in_game * 100, 1)
         ELSE NULL END AS league_pct_on_my_side,

    CASE WHEN b.status <> 'final' THEN NULL
         WHEN b.picked_home THEN (b.home_score - b.away_score) + b.line_home
         ELSE (b.away_score - b.home_score) - b.line_home END AS cover_margin,

    CASE WHEN b.status <> 'final' THEN 'pending'
         WHEN b.picked_home AND (b.home_score - b.away_score) + b.line_home > 0 THEN 'win'
         WHEN b.picked_home AND (b.home_score - b.away_score) + b.line_home = 0 THEN 'push'
         WHEN b.picked_home THEN 'loss'
         WHEN (b.away_score - b.home_score) - b.line_home > 0 THEN 'win'
         WHEN (b.away_score - b.home_score) - b.line_home = 0 THEN 'push'
         ELSE 'loss' END AS result
FROM base b;

-- Weekly record. tracked_rank only ranks entrants you store, so it is NOT your
-- pool finish -- cbs_reported_rank is. Both appear so a divergence is visible.
-- Pushes are excluded from win_pct rather than counted as half.
CREATE VIEW v_entrant_week AS
SELECT
    r.entrant_id, r.entrant, r.is_me, r.season_year, r.season_type, r.week_number,
    count(*) FILTER (WHERE r.result = 'win')     AS wins,
    count(*) FILTER (WHERE r.result = 'loss')    AS losses,
    count(*) FILTER (WHERE r.result = 'push')    AS pushes,
    count(*) FILTER (WHERE r.result = 'pending') AS pending,
    round(count(*) FILTER (WHERE r.result = 'win')::numeric
          / NULLIF(count(*) FILTER (WHERE r.result IN ('win','loss')), 0) * 100, 1) AS win_pct,
    rank() OVER (PARTITION BY r.season_year, r.season_type, r.week_number
                 ORDER BY count(*) FILTER (WHERE r.result = 'win') DESC) AS tracked_rank,
    max(ew.cbs_reported_rank) AS cbs_reported_rank,
    max(ew.cbs_reported_wins) AS cbs_reported_wins,
    max(w.pool_size)          AS pool_size
FROM v_pick_result r
JOIN week w ON w.season_year = r.season_year
            AND w.season_type = r.season_type
            AND w.week_number = r.week_number
LEFT JOIN entrant_week ew ON ew.entrant_id = r.entrant_id AND ew.week_id = w.week_id
GROUP BY r.entrant_id, r.entrant, r.is_me, r.season_year, r.season_type, r.week_number;

-- MNF tiebreaker. The actual total is derived from the score you already
-- ingest, so there is nothing to hand-enter and nothing to mistype.
-- guess_error is SIGNED: positive means you guessed high. Directional bias
-- shows up in roughly ten weeks; "how close am I" takes years.
CREATE VIEW v_tiebreak AS
SELECT
    e.display_name AS entrant, e.is_me,
    w.season_year, w.week_number,
    ew.tiebreak_guess,
    g.home_team_id, g.away_team_id,
    (g.home_score + g.away_score) AS actual_total,
    ew.tiebreak_guess - (g.home_score + g.away_score) AS guess_error,
    abs(ew.tiebreak_guess - (g.home_score + g.away_score)) AS abs_error
FROM entrant_week ew
JOIN entrant e ON e.entrant_id = ew.entrant_id
JOIN week w    ON w.week_id    = ew.week_id
LEFT JOIN game g ON g.game_id = w.tiebreak_game_id AND g.status = 'final'
WHERE ew.tiebreak_guess IS NOT NULL;

-- The weekly-prize view: your record bucketed by how much of the field agreed
-- with you. Picks where most of the field is with you cannot gain ground on
-- the pool; the low buckets are where weeks are won.
--
-- scope 'league' uses your league's number, 'public' uses CBS site-wide. Read
-- them separately -- they answer different questions and may disagree.
--
-- Expect small counts. A 30-pick bucket at 60% and one at 50% are not
-- distinguishable. Treat this as a record of what happened, not a signal,
-- until you have a few seasons behind it.
CREATE VIEW v_my_contrarian AS
SELECT season_year, 'league' AS scope,
       CASE WHEN league_pct_on_my_side >= 70 THEN 'with_field_70+'
            WHEN league_pct_on_my_side >= 55 THEN 'lean_with_55_69'
            WHEN league_pct_on_my_side >  45 THEN 'split_46_54'
            WHEN league_pct_on_my_side >= 30 THEN 'lean_against_30_45'
            ELSE 'against_field_under30' END AS field_bucket,
       count(*) FILTER (WHERE result IN ('win','loss')) AS graded,
       count(*) FILTER (WHERE result = 'win')           AS wins,
       round(count(*) FILTER (WHERE result = 'win')::numeric
             / NULLIF(count(*) FILTER (WHERE result IN ('win','loss')), 0) * 100, 1) AS win_pct
FROM v_pick_result
WHERE is_me AND league_pct_on_my_side IS NOT NULL
GROUP BY season_year, 3
UNION ALL
SELECT season_year, 'public',
       CASE WHEN public_pct_on_my_side >= 70 THEN 'with_field_70+'
            WHEN public_pct_on_my_side >= 55 THEN 'lean_with_55_69'
            WHEN public_pct_on_my_side >  45 THEN 'split_46_54'
            WHEN public_pct_on_my_side >= 30 THEN 'lean_against_30_45'
            ELSE 'against_field_under30' END,
       count(*) FILTER (WHERE result IN ('win','loss')),
       count(*) FILTER (WHERE result = 'win'),
       round(count(*) FILTER (WHERE result = 'win')::numeric
             / NULLIF(count(*) FILTER (WHERE result IN ('win','loss')), 0) * 100, 1)
FROM v_pick_result
WHERE is_me AND public_pct_on_my_side IS NOT NULL
GROUP BY season_year, 3;

-- Does the national number stand in for your league? One row per game where
-- both are known. If gap stays small across a few weeks, you can stop
-- importing leaguemates entirely and use the free number forever.
CREATE VIEW v_field_gap AS
SELECT w.season_year, w.week_number, g.game_id,
       ht.espn_abbr AS home_team, at.espn_abbr AS away_team,
       g.public_pct_home, g.league_pct_home,
       g.league_pct_home - g.public_pct_home AS gap
FROM game g
JOIN week w  ON w.week_id  = g.week_id
JOIN team ht ON ht.team_id = g.home_team_id
JOIN team at ON at.team_id = g.away_team_id
WHERE g.public_pct_home IS NOT NULL AND g.league_pct_home IS NOT NULL;
