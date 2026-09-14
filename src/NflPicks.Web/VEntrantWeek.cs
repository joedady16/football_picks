using System;
using System.Collections.Generic;

namespace NflPicks.Web;

public partial class VEntrantWeek
{
    public int? EntrantId { get; set; }

    public string? Entrant { get; set; }

    public bool? IsMe { get; set; }

    public short? SeasonYear { get; set; }

    public short? SeasonType { get; set; }

    public short? WeekNumber { get; set; }

    public long? Wins { get; set; }

    public long? Losses { get; set; }

    public long? Pushes { get; set; }

    public long? Pending { get; set; }

    public decimal? WinPct { get; set; }

    public long? TrackedRank { get; set; }

    public short? CbsReportedRank { get; set; }

    public short? CbsReportedWins { get; set; }

    public short? PoolSize { get; set; }
}
