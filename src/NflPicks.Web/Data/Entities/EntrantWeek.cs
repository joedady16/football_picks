using System;
using System.Collections.Generic;

namespace NflPicks.Web.Data.Entities;

public partial class EntrantWeek
{
    public int EntrantId { get; set; }

    public int WeekId { get; set; }

    public int? TiebreakGuess { get; set; }

    public short? CbsReportedWins { get; set; }

    public short? CbsReportedRank { get; set; }

    /// <summary>
    /// Season-to-date wins CBS reports for this entrant as of this week.
    /// </summary>
    public short? CbsSeasonWins { get; set; }

    public short? CbsSeasonRank { get; set; }

    public virtual Entrant Entrant { get; set; } = null!;

    public virtual Week Week { get; set; } = null!;
}
