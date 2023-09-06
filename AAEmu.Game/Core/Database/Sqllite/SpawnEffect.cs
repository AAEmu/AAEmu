using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SpawnEffect
{
    public long? Id { get; set; }

    public long? OwnerTypeId { get; set; }

    public long? SubType { get; set; }

    public long? PosDirId { get; set; }

    public double? PosAngle { get; set; }

    public double? PosDistance { get; set; }

    public long? OriDirId { get; set; }

    public double? OriAngle { get; set; }

    public byte[] UseSummonerFaction { get; set; }

    public double? LifeTime { get; set; }

    public byte[] DespawnOnCreatorDeath { get; set; }

    public byte[] UseSummonerAggroTarget { get; set; }

    public long? MateStateId { get; set; }
}
