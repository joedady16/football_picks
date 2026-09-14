using System;
using System.Collections.Generic;

namespace NflPicks.Web;

public partial class Week
{
    public int WeekId { get; set; }

    public short SeasonYear { get; set; }

    public short SeasonType { get; set; }

    public short WeekNumber { get; set; }

    public DateTime? SpreadsPostedAt { get; set; }

    public short? PoolSize { get; set; }

    public int? TiebreakGameId { get; set; }

    public virtual ICollection<EntrantWeek> EntrantWeeks { get; set; } = new List<EntrantWeek>();

    public virtual ICollection<Game> Games { get; set; } = new List<Game>();

    public virtual Game? TiebreakGame { get; set; }
}
