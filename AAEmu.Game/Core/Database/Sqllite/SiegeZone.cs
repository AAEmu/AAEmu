using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SiegeZone
{
    public long? Id { get; set; }

    public long? StartSiegeWeekday { get; set; }

    public long? StartSiegeHour { get; set; }

    public long? StartSiegeMin { get; set; }

    public long? SiegeDays { get; set; }

    public long? SiegeHours { get; set; }

    public long? SiegeMins { get; set; }

    public long? ZoneGroupId { get; set; }

    public long? PayWeekday { get; set; }

    public long? PayHour { get; set; }

    public long? PayMin { get; set; }

    public long? DeclareItemId { get; set; }

    public long? DefenseTicketId { get; set; }

    public long? OffenseTicketId { get; set; }

    public long? ReinforceDefenseDelayMins { get; set; }

    public long? DefenseMerchantId { get; set; }

    public long? OffenseMerchantId { get; set; }

    public long? DominionMerchantId { get; set; }

    public long? OpenHour { get; set; }

    public long? OpenDurationHours { get; set; }

    public long? StartAuctionWeekday { get; set; }

    public long? StartAuctionHour { get; set; }

    public long? StartAuctionMin { get; set; }

    public long? StartDeclareWeekday { get; set; }

    public long? StartDeclareHour { get; set; }

    public long? StartDeclareMin { get; set; }

    public long? StartWarmupWeekday { get; set; }

    public long? StartWarmupHour { get; set; }

    public long? StartWarmupMin { get; set; }

    public long? OpenWeekday { get; set; }

    public long? MonumentDoodadId { get; set; }
}
