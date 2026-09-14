using System;
using System.Collections.Generic;

namespace NflPicks.Web;

public partial class Entrant
{
    public int EntrantId { get; set; }

    public string DisplayName { get; set; } = null!;

    public bool IsMe { get; set; }

    public bool IsActive { get; set; }

    public bool IsTracked { get; set; }

    public string? Note { get; set; }

    public virtual ICollection<EntrantWeek> EntrantWeeks { get; set; } = new List<EntrantWeek>();

    public virtual ICollection<Pick> Picks { get; set; } = new List<Pick>();
}
