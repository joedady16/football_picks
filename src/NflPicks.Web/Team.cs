using System;
using System.Collections.Generic;

namespace NflPicks.Web;

public partial class Team
{
    public short TeamId { get; set; }

    public string EspnAbbr { get; set; } = null!;

    public int? EspnTeamId { get; set; }

    public string FullName { get; set; } = null!;

    public string Conference { get; set; } = null!;

    public string Division { get; set; } = null!;

    public virtual ICollection<Game> GameAwayTeams { get; set; } = new List<Game>();

    public virtual ICollection<Game> GameHomeTeams { get; set; } = new List<Game>();

    public virtual ICollection<PickChange> PickChangeFromTeams { get; set; } = new List<PickChange>();

    public virtual ICollection<PickChange> PickChangeToTeams { get; set; } = new List<PickChange>();

    public virtual ICollection<Pick> Picks { get; set; } = new List<Pick>();
}
