using System;
using System.Collections.Generic;

namespace NflPicks.Web;

public partial class Game
{
    public int GameId { get; set; }

    public int WeekId { get; set; }

    public long? EspnEventId { get; set; }

    public short HomeTeamId { get; set; }

    public short AwayTeamId { get; set; }

    public DateTime KickoffUtc { get; set; }

    public decimal? GradingLineHome { get; set; }

    public decimal? PublicPctHome { get; set; }

    public decimal? LeaguePctHome { get; set; }

    public DateTime? PctAsOf { get; set; }

    public short? HomeScore { get; set; }

    public short? AwayScore { get; set; }

    public string Status { get; set; } = null!;

    public string? ScoreSource { get; set; }

    public DateTime? ScoresUpdatedAt { get; set; }

    public virtual Team AwayTeam { get; set; } = null!;

    public virtual Team HomeTeam { get; set; } = null!;

    public virtual ICollection<Pick> Picks { get; set; } = new List<Pick>();

    public virtual Week Week { get; set; } = null!;

    public virtual ICollection<Week> Weeks { get; set; } = new List<Week>();
}
