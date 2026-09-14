using System;
using System.Collections.Generic;

namespace NflPicks.Web.Data.Entities;

public partial class Pick
{
    public int PickId { get; set; }

    public int EntrantId { get; set; }

    public int GameId { get; set; }

    public short PickedTeamId { get; set; }

    public DateTime FirstPickedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string Source { get; set; } = null!;

    public string? Note { get; set; }

    public virtual Entrant Entrant { get; set; } = null!;

    public virtual Game Game { get; set; } = null!;

    public virtual ICollection<PickChange> PickChanges { get; set; } = new List<PickChange>();

    public virtual Team PickedTeam { get; set; } = null!;
}
