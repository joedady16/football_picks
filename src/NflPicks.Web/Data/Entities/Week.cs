using System;
using System.Collections.Generic;

namespace NflPicks.Web.Data.Entities;

public partial class Week
{
    public int WeekId { get; set; }

    public short SeasonYear { get; set; }

    public short SeasonType { get; set; }

    public short WeekNumber { get; set; }

    public DateTime? SpreadsPostedAt { get; set; }

    public short? PoolSize { get; set; }

    public int? TiebreakGameId { get; set; }

    /// <summary>
    /// Rank you are treating as the prize cut, e.g. 9 for top 10% of ~90.
    /// </summary>
    public short? CutoffRank { get; set; }

    /// <summary>
    /// Season-to-date wins of the entrant sitting at cutoff_rank, as of this week.
    /// </summary>
    public short? CutoffSeasonWins { get; set; }

    /// <summary>
    /// Season-to-date wins of the pool leader, as of this week.
    /// </summary>
    public short? LeaderSeasonWins { get; set; }

    public virtual ICollection<EntrantWeek> EntrantWeeks { get; set; } = new List<EntrantWeek>();

    public virtual ICollection<Game> Games { get; set; } = new List<Game>();

    public virtual Game? TiebreakGame { get; set; }
}
