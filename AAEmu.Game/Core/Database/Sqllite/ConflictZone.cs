using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ConflictZone
{
    public long? ZoneGroupId { get; set; }

    public long? NumKills0 { get; set; }

    public long? NumKills1 { get; set; }

    public long? NumKills2 { get; set; }

    public long? NumKills3 { get; set; }

    public long? NumKills4 { get; set; }

    public long? NoKillMin0 { get; set; }

    public long? NoKillMin1 { get; set; }

    public long? NoKillMin2 { get; set; }

    public long? NoKillMin3 { get; set; }

    public long? NoKillMin4 { get; set; }

    public long? ConflictMin { get; set; }

    public long? WarMin { get; set; }

    public long? PeaceMin { get; set; }

    public long? PeaceProtectedFactionId { get; set; }

    public long? NuiaReturnPointId { get; set; }

    public long? HariharaReturnPointId { get; set; }

    public long? WarTowerDefId { get; set; }

    public long? PeaceTowerDefId { get; set; }

    public byte[] Closed { get; set; }
}
