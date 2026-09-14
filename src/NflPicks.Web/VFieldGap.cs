using System;
using System.Collections.Generic;

namespace NflPicks.Web;

public partial class VFieldGap
{
    public short? SeasonYear { get; set; }

    public short? WeekNumber { get; set; }

    public int? GameId { get; set; }

    public string? HomeTeam { get; set; }

    public string? AwayTeam { get; set; }

    public decimal? PublicPctHome { get; set; }

    public decimal? LeaguePctHome { get; set; }

    public decimal? Gap { get; set; }
}
