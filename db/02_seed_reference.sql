-- Reference data. Runs after 01_schema.sql on first container init.
--
-- espn_abbr matches ESPN's scoreboard feed and is the join key for ingestion.
-- The ones that catch people out: WSH (not WAS), LV (not LVR), JAX (not JAC),
-- and the LA pair, LAR / LAC.
--
-- espn_team_id is deliberately left NULL -- the Phase 3 ingest fills it in on
-- first run rather than us hardcoding IDs that might be wrong.

INSERT INTO team (team_id, espn_abbr, full_name, conference, division) VALUES
    ( 1, 'BUF', 'Buffalo Bills',         'AFC', 'East'),
    ( 2, 'MIA', 'Miami Dolphins',        'AFC', 'East'),
    ( 3, 'NE',  'New England Patriots',  'AFC', 'East'),
    ( 4, 'NYJ', 'New York Jets',         'AFC', 'East'),
    ( 5, 'BAL', 'Baltimore Ravens',      'AFC', 'North'),
    ( 6, 'CIN', 'Cincinnati Bengals',    'AFC', 'North'),
    ( 7, 'CLE', 'Cleveland Browns',      'AFC', 'North'),
    ( 8, 'PIT', 'Pittsburgh Steelers',   'AFC', 'North'),
    ( 9, 'HOU', 'Houston Texans',        'AFC', 'South'),
    (10, 'IND', 'Indianapolis Colts',    'AFC', 'South'),
    (11, 'JAX', 'Jacksonville Jaguars',  'AFC', 'South'),
    (12, 'TEN', 'Tennessee Titans',      'AFC', 'South'),
    (13, 'DEN', 'Denver Broncos',        'AFC', 'West'),
    (14, 'KC',  'Kansas City Chiefs',    'AFC', 'West'),
    (15, 'LV',  'Las Vegas Raiders',     'AFC', 'West'),
    (16, 'LAC', 'Los Angeles Chargers',  'AFC', 'West'),
    (17, 'DAL', 'Dallas Cowboys',        'NFC', 'East'),
    (18, 'NYG', 'New York Giants',       'NFC', 'East'),
    (19, 'PHI', 'Philadelphia Eagles',   'NFC', 'East'),
    (20, 'WSH', 'Washington Commanders', 'NFC', 'East'),
    (21, 'CHI', 'Chicago Bears',         'NFC', 'North'),
    (22, 'DET', 'Detroit Lions',         'NFC', 'North'),
    (23, 'GB',  'Green Bay Packers',     'NFC', 'North'),
    (24, 'MIN', 'Minnesota Vikings',     'NFC', 'North'),
    (25, 'ATL', 'Atlanta Falcons',       'NFC', 'South'),
    (26, 'CAR', 'Carolina Panthers',     'NFC', 'South'),
    (27, 'NO',  'New Orleans Saints',    'NFC', 'South'),
    (28, 'TB',  'Tampa Bay Buccaneers',  'NFC', 'South'),
    (29, 'ARI', 'Arizona Cardinals',     'NFC', 'West'),
    (30, 'LAR', 'Los Angeles Rams',      'NFC', 'West'),
    (31, 'SF',  'San Francisco 49ers',   'NFC', 'West'),
    (32, 'SEA', 'Seattle Seahawks',      'NFC', 'West');

-- Your entrant row. Change 'Me' to your exact CBS screen name before you
-- import anyone else's picks -- the importer matches on this string, and a
-- mismatch creates a duplicate entrant instead of raising an error.
INSERT INTO entrant (display_name, is_me) VALUES ('Me', true);
