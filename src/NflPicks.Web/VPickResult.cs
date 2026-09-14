using System;
using System.Collections.Generic;

namespace NflPicks.Web;

public partial class VPickResult
{
    public int? PickId { get; set; }

    public string? Note { get; set; }

    public DateTime? FirstPickedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? Source { get; set; }

    public int? EntrantId { get; set; }

    public string? Entrant { get; set; }

    public bool? IsMe { get; set; }

    public short? SeasonYear { get; set; }

    public short? SeasonType { get; set; }

    public short? WeekNumber { get; set; }

    public int? GameId { get; set; }

    public DateTime? KickoffUtc { get; set; }

    public string? Status { get; set; }

    public short? HomeScore { get; set; }

    public short? AwayScore { get; set; }

    public decimal? LineHome { get; set; }

    public decimal? PublicPctHome { get; set; }

    public decimal? LeaguePctHome { get; set; }

    public string? HomeTeam { get; set; }

    public string? AwayTeam { get; set; }

    public string? PickedTeam { get; set; }

    public bool? PickedHome { get; set; }

    public bool? IsDivisional { get; set; }

    public decimal? LineForPick { get; set; }

    public long? TrackedInGame { get; set; }

    public long? TrackedOnHome { get; set; }

    public bool? WasChanged { get; set; }

    public string? PickSide { get; set; }

    public string? LineBucket { get; set; }

    public bool? IsPrimetime { get; set; }

    public string? KickoffDayEt { get; set; }

    public decimal? PublicPctOnMySide { get; set; }

    public string? LeaguePctSource { get; set; }

    public decimal? LeaguePctOnMySide { get; set; }

    public decimal? CoverMargin { get; set; }

    public string? Result { get; set; }
}
