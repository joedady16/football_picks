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

    public virtual Entrant Entrant { get; set; } = null!;

    public virtual Week Week { get; set; } = null!;
}
