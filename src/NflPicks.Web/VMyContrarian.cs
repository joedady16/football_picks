using System;
using System.Collections.Generic;

namespace NflPicks.Web;

public partial class VMyContrarian
{
    public short? SeasonYear { get; set; }

    public string? Scope { get; set; }

    public string? FieldBucket { get; set; }

    public long? Graded { get; set; }

    public long? Wins { get; set; }

    public decimal? WinPct { get; set; }
}
