# NFL Pick Tracker

Personal tracker for a CBS Sports ATS pick'em pool (~90 entrants, one weekly
winner, season-long top-10 prizes). Records picks, grades them against the
spread, and tracks position relative to the pool.

Blazor Server on .NET, PostgreSQL in Docker, EF Core database-first.

---

## Weekly routine

**Tuesday** — CBS posts the new board and updates standings from the week just
scored.

1. `/week` — load the upcoming week, set the Sunday date, pool size
2. Fill the grey **Last week** strip with five numbers off the CBS standings
   page: leader's season wins, cut rank, wins at the cut, your season wins,
   your rank. Cut rank carries forward automatically after the first time.
3. Enter matchups and kickoff slots. Lines can wait if CBS hasn't posted them.
4. Save. The confirmation says how many games still have no line — those are
   blocked for picks until the line is entered.

**Any time before kickoff** — `/picks` to make or change picks. Each game locks
at its own kickoff, matching the pool's rules. Changes are logged to
`pick_change` with hours remaining, so "do my second thoughts beat my first
instinct" becomes answerable eventually.

**Sunday night / Monday** — `/week`, enter final scores. Both scores or neither;
filling both marks the game final and triggers grading.

**Any time** — `/season` for the standing, the gap to the cut line, and splits.

---

## Running it

```bash
docker compose up -d          # Postgres on host port 5433
```

Then F5 in Visual Studio. The app connects via user secrets
(`ConnectionStrings:Picks`), so the password is never in the repo.

`/dbcheck` answers "is it the app or the database" in one click. Seven tables,
seven views, 32 teams means everything is wired.

### Things that will have been forgotten

**`docker compose down` does not re-run the init scripts.** They execute only
against an empty data directory. Use `down -v` to drop the volume — which also
destroys all data, so after the first season that is not something to do
casually. Use a migration instead.

**Schema changes go through `db/migrations/`,** then get copied to `db/init/`
with the next number. `db/init/` is baseline plus every migration in order, so
a fresh database and a migrated one end up identical. If those two ever drift,
a rebuild silently produces a database missing columns.

```bash
docker exec -i nflpicks-db psql -U picks -d nflpicks -v ON_ERROR_STOP=1 \
    < db/migrations/00X_whatever.sql
```

`ON_ERROR_STOP=1` matters. Without it psql ploughs past failures and leaves a
half-applied migration. `schema_migration` records what has run.

**Order for any schema change: migrate, scaffold, then write code.** Doing it
the other way round deadlocks — the scaffolder builds the project before it
runs, and the project will not build while it references columns the entities
do not have yet. The PowerShell cmdlet has no `--no-build` escape hatch.

```powershell
Scaffold-DbContext "Name=ConnectionStrings:Picks" Npgsql.EntityFrameworkCore.PostgreSQL -OutputDir Data/Entities -ContextDir Data -Context PicksDbContext -Tables team,entrant,week,game,pick,pick_change,entrant_week -NoOnConfiguring -Force
```

**After any scaffold, check for duplicate entity files.**
`find . -name "Week.cs" -not -path "*/obj/*"` should return exactly one path.
Orphaned output from an earlier run merges silently — same namespace, same
`partial class` — and the compiler binds to stale definitions while the context
looks correct. This cost hours once.

Views are deliberately excluded from the EF model. They are queried as raw SQL
through the shared `NpgsqlDataSource`, because the analysis queries use window
functions and grouping that LINQ would only obscure.

---

## Data model notes

**Lines are always from the home team's perspective.** `-3.5` means the home
team is favoured by 3.5. This is the single most important convention in the
schema; a flipped sign grades every pick on that game backwards.

**`game.grading_line_home` cannot be reconstructed later.** CBS sets its own
spreads, they are not market lines, and the historical value is not published
anywhere. If it is not captured the week it is posted, that game is permanently
ungradeable. A trigger refuses to store any pick against a game with no line.

**Two consensus numbers, kept separate.** `public_pct_home` is CBS's site-wide
percentage — the national user base, not this pool. `league_pct_home` is the
pool's own share, currently unused. They are not merged because they answer
different questions, and `v_field_gap` exists to measure whether the free one
is a usable stand-in.

**Scores marked `manual` are never overwritten** by any ingest.

### ESPN ingestion is abandoned

`Services/Espn/` contains a scoreboard client that does not work. Akamai blocks
.NET's TLS fingerprint while curl from the same machine and IP succeeds against
the identical URL. No combination of headers changes it. Scores are entered by
hand instead, which takes about two minutes a week.

Kept in the tree because the archive-then-parse structure is worth reusing if a
properly documented sports API is ever wired up. It is not a half-finished
feature.

---

## Reading the analysis honestly

The splits on `/season` will be thin for a long time. A full season is roughly
270 picks; sliced by favourite/dog or by line bucket, most buckets hold 20-40
graded picks, and at that size a single game moves the win rate several points.
Buckets under 25 are flagged *thin* for this reason.

They are a record of what happened, not a signal to act on, until the counts get
large. That is a feature of the honest version of this app, not a limitation to
engineer around — collecting from week one is exactly what makes them readable
in a few seasons.

`v_my_contrarian` deserves a specific caveat. Fading the public creates
separation, which wins individual weeks but adds variance. The season-long
top-10 goal rewards the opposite: pick the best side every time and let accuracy
compound. So treat that view as a question about judgment ("how do I do when I
disagree with the public") rather than a lever to pull.

**`reconcile` on `/season` is the one column to actually act on.** A red
MISMATCH means your computed record and CBS's disagree, which is almost always a
typo in a score or a missing pick. Chase it the week it appears.

---

## Layout

```
db/
  init/          baseline + migrations, run on first container start only
  migrations/    applied to live databases
  smoke_test.sql grading arithmetic check, rolls back
src/NflPicks.Web/
  Components/Pages/   week setup, picks, season, dbcheck
  Data/Entities/      scaffolded — never hand-edit, use partial classes
  Services/Espn/      abandoned, see above
```
```
**Notes matter.** The "Why" box on each pick locks at kickoff. A season of
short reasons is the only data here that no SQL query can produce — it makes
"which kinds of reasoning actually work for me" answerable in December. Keep
them short and categorizable.
```