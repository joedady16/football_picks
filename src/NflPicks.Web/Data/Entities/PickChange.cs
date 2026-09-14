using System;
using System.Collections.Generic;

namespace NflPicks.Web.Data.Entities;

public partial class PickChange
{
    public int ChangeId { get; set; }

    public int PickId { get; set; }

    public short FromTeamId { get; set; }

    public short ToTeamId { get; set; }

    public DateTime ChangedAt { get; set; }

    public decimal? HoursBeforeKickoff { get; set; }

    public virtual Team FromTeam { get; set; } = null!;

    public virtual Pick Pick { get; set; } = null!;

    public virtual Team ToTeam { get; set; } = null!;
}
