-- Phase 1 verification. Lives OUTSIDE db/init so it never runs on startup.
-- Everything is inside a transaction that rolls back: your data is untouched.
--
--   docker exec -i nflpicks-db psql -U picks -d nflpicks < db/smoke_test.sql

BEGIN;

INSERT INTO entrant (display_name) VALUES ('TestRival1'), ('TestRival2');

INSERT INTO week (season_year, season_type, week_number, spreads_posted_at, pool_size)
VALUES (9999, 2, 1, now(), 90);

-- Four finals. public_pct_home is the national number; league_pct_home is
-- entered for two games and left null for two, to exercise both paths.
INSERT INTO game (week_id, home_team_id, away_team_id, kickoff_utc,
                  grading_line_home, public_pct_home, league_pct_home,
                  status, home_score, away_score, score_source)
SELECT w.week_id, v.h, v.a, v.ko, v.line, v.pub, v.lg, 'final', v.hs, v.asc_, 'manual'
FROM week w, (VALUES
    (14, 15, TIMESTAMPTZ '2026-09-20 17:00:00Z', -3.5, 82.0, 78.0, 24, 17),  -- KC 24, LV 17
    (19, 17, TIMESTAMPTZ '2026-09-20 17:00:00Z', -3.5, 61.0, NULL, 21, 14),  -- PHI 21, DAL 14
    ( 1,  2, TIMESTAMPTZ '2026-09-20 17:00:00Z', -7.0, 55.0, NULL, 28, 21),  -- BUF 28, MIA 21
    (21, 22, TIMESTAMPTZ '2026-09-22 00:15:00Z',  6.5, 22.0, 31.0, 20, 23)   -- CHI 20, DET 23 (MNF)
) AS v(h, a, ko, line, pub, lg, hs, asc_)
WHERE w.season_year = 9999 AND w.week_number = 1;

-- The CHI/DET game is Monday night, so it is the tiebreaker game.
UPDATE week SET tiebreak_game_id = (
    SELECT g.game_id FROM game g WHERE g.week_id = week.week_id AND g.home_team_id = 21
) WHERE season_year = 9999 AND week_number = 1;

-- Your four picks:
--   KC  -3.5  wins by 7        -> win   (field heavily with you: no ground gained)
--   DAL +3.5  loses by 7       -> loss
--   MIA +7    loses by 7       -> push
--   CHI +6.5  loses by 3       -> win   (field against you: this is how weeks are won)
INSERT INTO pick (entrant_id, game_id, picked_team_id, note)
SELECT e.entrant_id, g.game_id, v.picked, v.label
FROM entrant e, game g
JOIN week w ON w.week_id = g.week_id
JOIN (VALUES
    (14, 14, '1 home fav -3.5, wins by 7'),
    (17, 19, '2 road dog +3.5, loses by 7'),
    ( 2,  1, '3 road dog +7, loses by exactly 7'),
    (21, 21, '4 home dog +6.5, loses by 3')
) AS v(picked, home_ref, label) ON v.home_ref = g.home_team_id
WHERE e.is_me AND w.season_year = 9999 AND w.week_number = 1;

INSERT INTO entrant_week (entrant_id, week_id, tiebreak_guess, cbs_reported_wins, cbs_reported_rank)
SELECT e.entrant_id, w.week_id, 47, 3, 12
FROM entrant e, week w
WHERE e.is_me AND w.season_year = 9999 AND w.week_number = 1;

\echo ''
\echo '=== 1. grading: expect win, loss, push, win ==='
SELECT note, picked_team, line_for_pick, pick_side, cover_margin, result
FROM v_pick_result
WHERE is_me AND season_year = 9999 AND week_number = 1
ORDER BY note;

\echo ''
\echo '=== 2. consensus: public 82.0/39.0/45.0/22.0 ==='
\echo '===    league 78.0 (entered), null, null, 31.0 (entered) ==='
SELECT note, public_pct_on_my_side, league_pct_on_my_side, league_pct_source
FROM v_pick_result
WHERE is_me AND season_year = 9999 AND week_number = 1
ORDER BY note;

\echo ''
\echo '=== 3. national vs league gap: expect -4.0 on KC, +9.0 on CHI ==='
SELECT home_team, away_team, public_pct_home, league_pct_home, gap
FROM v_field_gap WHERE season_year = 9999 ORDER BY home_team;

\echo ''
\echo '=== 4. contrarian buckets, league and public scopes ==='
SELECT scope, field_bucket, graded, wins, win_pct
FROM v_my_contrarian WHERE season_year = 9999 ORDER BY scope, field_bucket;

\echo ''
\echo '=== 5. weekly record: expect 3-1, cbs_reported_rank 12, pool_size 90 ==='
SELECT entrant, wins, losses, pushes, win_pct, cbs_reported_rank, pool_size
FROM v_entrant_week WHERE season_year = 9999 AND week_number = 1;

\echo ''
\echo '=== 6. tiebreaker: guess 47, actual 43, guess_error +4 (guessed high) ==='
SELECT entrant, tiebreak_guess, actual_total, guess_error, abs_error
FROM v_tiebreak WHERE season_year = 9999 AND week_number = 1;

\echo ''
\echo '=== 7. audit: flip CHI -> DET, expect one row with timing ==='
UPDATE pick SET picked_team_id = 22
WHERE picked_team_id = 21 AND entrant_id = (SELECT entrant_id FROM entrant WHERE is_me);

SELECT ft.espn_abbr AS from_team, tt.espn_abbr AS to_team,
       (pc.hours_before_kickoff IS NOT NULL) AS has_timing
FROM pick_change pc
JOIN team ft ON ft.team_id = pc.from_team_id
JOIN team tt ON tt.team_id = pc.to_team_id;

\echo ''
\echo '=== 8. trigger: team not in game, MUST ERROR ==='
SAVEPOINT sp1;
INSERT INTO pick (entrant_id, game_id, picked_team_id)
SELECT e.entrant_id, min(g.game_id), 32
FROM entrant e, game g JOIN week w ON w.week_id = g.week_id
WHERE e.display_name = 'TestRival2' AND w.week_number = 1
GROUP BY e.entrant_id;
ROLLBACK TO SAVEPOINT sp1;

\echo ''
\echo '=== 9. trigger: pick on a game with no line, MUST ERROR ==='
SAVEPOINT sp2;
INSERT INTO game (week_id, home_team_id, away_team_id, kickoff_utc)
SELECT week_id, 31, 32, TIMESTAMPTZ '2026-09-20 20:00:00Z'
FROM week WHERE season_year = 9999 AND week_number = 1;
INSERT INTO pick (entrant_id, game_id, picked_team_id)
SELECT e.entrant_id, g.game_id, 31
FROM entrant e, game g
WHERE e.is_me AND g.home_team_id = 31 AND g.grading_line_home IS NULL;
ROLLBACK TO SAVEPOINT sp2;

\echo ''
\echo '=== end: everything above is rolled back ==='
ROLLBACK;
