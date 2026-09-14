using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using NflPicks.Web.Data.Entities;

namespace NflPicks.Web.Data;

public partial class PicksDbContext : DbContext
{
    public PicksDbContext(DbContextOptions<PicksDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Entrant> Entrants { get; set; }

    public virtual DbSet<EntrantWeek> EntrantWeeks { get; set; }

    public virtual DbSet<Game> Games { get; set; }

    public virtual DbSet<Pick> Picks { get; set; }

    public virtual DbSet<PickChange> PickChanges { get; set; }

    public virtual DbSet<Team> Teams { get; set; }

    public virtual DbSet<Week> Weeks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entrant>(entity =>
        {
            entity.HasKey(e => e.EntrantId).HasName("entrant_pkey");

            entity.ToTable("entrant");

            entity.HasIndex(e => e.DisplayName, "entrant_display_name_key").IsUnique();

            entity.HasIndex(e => e.IsMe, "ux_entrant_is_me")
                .IsUnique()
                .HasFilter("is_me");

            entity.Property(e => e.EntrantId).HasColumnName("entrant_id");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsMe).HasColumnName("is_me");
            entity.Property(e => e.IsTracked)
                .HasDefaultValue(true)
                .HasColumnName("is_tracked");
            entity.Property(e => e.Note).HasColumnName("note");
        });

        modelBuilder.Entity<EntrantWeek>(entity =>
        {
            entity.HasKey(e => new { e.EntrantId, e.WeekId }).HasName("entrant_week_pkey");

            entity.ToTable("entrant_week");

            entity.Property(e => e.EntrantId).HasColumnName("entrant_id");
            entity.Property(e => e.WeekId).HasColumnName("week_id");
            entity.Property(e => e.CbsReportedRank).HasColumnName("cbs_reported_rank");
            entity.Property(e => e.CbsReportedWins).HasColumnName("cbs_reported_wins");
            entity.Property(e => e.TiebreakGuess).HasColumnName("tiebreak_guess");

            entity.HasOne(d => d.Entrant).WithMany(p => p.EntrantWeeks)
                .HasForeignKey(d => d.EntrantId)
                .HasConstraintName("entrant_week_entrant_id_fkey");

            entity.HasOne(d => d.Week).WithMany(p => p.EntrantWeeks)
                .HasForeignKey(d => d.WeekId)
                .HasConstraintName("entrant_week_week_id_fkey");
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(e => e.GameId).HasName("game_pkey");

            entity.ToTable("game");

            entity.HasIndex(e => e.EspnEventId, "game_espn_event_id_key").IsUnique();

            entity.HasIndex(e => e.KickoffUtc, "ix_game_kickoff");

            entity.HasIndex(e => e.WeekId, "ix_game_week");

            entity.Property(e => e.GameId).HasColumnName("game_id");
            entity.Property(e => e.AwayScore).HasColumnName("away_score");
            entity.Property(e => e.AwayTeamId).HasColumnName("away_team_id");
            entity.Property(e => e.EspnEventId).HasColumnName("espn_event_id");
            entity.Property(e => e.GradingLineHome)
                .HasPrecision(4, 1)
                .HasColumnName("grading_line_home");
            entity.Property(e => e.HomeScore).HasColumnName("home_score");
            entity.Property(e => e.HomeTeamId).HasColumnName("home_team_id");
            entity.Property(e => e.KickoffUtc).HasColumnName("kickoff_utc");
            entity.Property(e => e.LeaguePctHome)
                .HasPrecision(5, 2)
                .HasColumnName("league_pct_home");
            entity.Property(e => e.PctAsOf).HasColumnName("pct_as_of");
            entity.Property(e => e.PublicPctHome)
                .HasPrecision(5, 2)
                .HasColumnName("public_pct_home");
            entity.Property(e => e.ScoreSource).HasColumnName("score_source");
            entity.Property(e => e.ScoresUpdatedAt).HasColumnName("scores_updated_at");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'scheduled'::text")
                .HasColumnName("status");
            entity.Property(e => e.WeekId).HasColumnName("week_id");

            entity.HasOne(d => d.AwayTeam).WithMany(p => p.GameAwayTeams)
                .HasForeignKey(d => d.AwayTeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("game_away_team_id_fkey");

            entity.HasOne(d => d.HomeTeam).WithMany(p => p.GameHomeTeams)
                .HasForeignKey(d => d.HomeTeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("game_home_team_id_fkey");

            entity.HasOne(d => d.Week).WithMany(p => p.Games)
                .HasForeignKey(d => d.WeekId)
                .HasConstraintName("game_week_id_fkey");
        });

        modelBuilder.Entity<Pick>(entity =>
        {
            entity.HasKey(e => e.PickId).HasName("pick_pkey");

            entity.ToTable("pick");

            entity.HasIndex(e => e.GameId, "ix_pick_game");

            entity.HasIndex(e => e.PickedTeamId, "ix_pick_team");

            entity.HasIndex(e => new { e.EntrantId, e.GameId }, "ux_pick_entrant_game").IsUnique();

            entity.Property(e => e.PickId).HasColumnName("pick_id");
            entity.Property(e => e.EntrantId).HasColumnName("entrant_id");
            entity.Property(e => e.FirstPickedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("first_picked_at");
            entity.Property(e => e.GameId).HasColumnName("game_id");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.PickedTeamId).HasColumnName("picked_team_id");
            entity.Property(e => e.Source)
                .HasDefaultValueSql("'manual'::text")
                .HasColumnName("source");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Entrant).WithMany(p => p.Picks)
                .HasForeignKey(d => d.EntrantId)
                .HasConstraintName("pick_entrant_id_fkey");

            entity.HasOne(d => d.Game).WithMany(p => p.Picks)
                .HasForeignKey(d => d.GameId)
                .HasConstraintName("pick_game_id_fkey");

            entity.HasOne(d => d.PickedTeam).WithMany(p => p.Picks)
                .HasForeignKey(d => d.PickedTeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("pick_picked_team_id_fkey");
        });

        modelBuilder.Entity<PickChange>(entity =>
        {
            entity.HasKey(e => e.ChangeId).HasName("pick_change_pkey");

            entity.ToTable("pick_change");

            entity.HasIndex(e => e.PickId, "ix_pick_change_pick");

            entity.Property(e => e.ChangeId).HasColumnName("change_id");
            entity.Property(e => e.ChangedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("changed_at");
            entity.Property(e => e.FromTeamId).HasColumnName("from_team_id");
            entity.Property(e => e.HoursBeforeKickoff)
                .HasPrecision(6, 2)
                .HasColumnName("hours_before_kickoff");
            entity.Property(e => e.PickId).HasColumnName("pick_id");
            entity.Property(e => e.ToTeamId).HasColumnName("to_team_id");

            entity.HasOne(d => d.FromTeam).WithMany(p => p.PickChangeFromTeams)
                .HasForeignKey(d => d.FromTeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("pick_change_from_team_id_fkey");

            entity.HasOne(d => d.Pick).WithMany(p => p.PickChanges)
                .HasForeignKey(d => d.PickId)
                .HasConstraintName("pick_change_pick_id_fkey");

            entity.HasOne(d => d.ToTeam).WithMany(p => p.PickChangeToTeams)
                .HasForeignKey(d => d.ToTeamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("pick_change_to_team_id_fkey");
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(e => e.TeamId).HasName("team_pkey");

            entity.ToTable("team");

            entity.HasIndex(e => e.EspnAbbr, "team_espn_abbr_key").IsUnique();

            entity.HasIndex(e => e.EspnTeamId, "team_espn_team_id_key").IsUnique();

            entity.Property(e => e.TeamId)
                .ValueGeneratedNever()
                .HasColumnName("team_id");
            entity.Property(e => e.Conference).HasColumnName("conference");
            entity.Property(e => e.Division).HasColumnName("division");
            entity.Property(e => e.EspnAbbr).HasColumnName("espn_abbr");
            entity.Property(e => e.EspnTeamId).HasColumnName("espn_team_id");
            entity.Property(e => e.FullName).HasColumnName("full_name");
        });

        modelBuilder.Entity<Week>(entity =>
        {
            entity.HasKey(e => e.WeekId).HasName("week_pkey");

            entity.ToTable("week");

            entity.HasIndex(e => new { e.SeasonYear, e.SeasonType, e.WeekNumber }, "week_season_year_season_type_week_number_key").IsUnique();

            entity.Property(e => e.WeekId).HasColumnName("week_id");
            entity.Property(e => e.PoolSize).HasColumnName("pool_size");
            entity.Property(e => e.SeasonType)
                .HasDefaultValue((short)2)
                .HasColumnName("season_type");
            entity.Property(e => e.SeasonYear).HasColumnName("season_year");
            entity.Property(e => e.SpreadsPostedAt).HasColumnName("spreads_posted_at");
            entity.Property(e => e.TiebreakGameId).HasColumnName("tiebreak_game_id");
            entity.Property(e => e.WeekNumber).HasColumnName("week_number");

            entity.HasOne(d => d.TiebreakGame).WithMany(p => p.Weeks)
                .HasForeignKey(d => d.TiebreakGameId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_week_tiebreak_game");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
