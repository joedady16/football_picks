using System;
using System.Collections.Generic;

namespace NflPicks.Web;

public partial class VTiebreak
{
    public string? Entrant { get; set; }

    public bool? IsMe { get; set; }

    public short? SeasonYear { get; set; }

    public short? WeekNumber { get; set; }

    public int? TiebreakGuess { get; set; }

    public short? HomeTeamId { get; set; }

    public short? AwayTeamId { get; set; }

    public short? ActualTotal { get; set; }

    public int? GuessError { get; set; }

    public int? AbsError { get; set; }
}
