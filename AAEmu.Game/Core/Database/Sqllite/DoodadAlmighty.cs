using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadAlmighty
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Model { get; set; }

    public byte[] OnceOneMan { get; set; }

    public byte[] OnceOneInteraction { get; set; }

    public byte[] ShowName { get; set; }

    public byte[] MgmtSpawn { get; set; }

    public long? Percent { get; set; }

    public long? MinTime { get; set; }

    public long? MaxTime { get; set; }

    public long? ModelKindId { get; set; }

    public byte[] UseCreatorFaction { get; set; }

    public byte[] ForceTodTopPriority { get; set; }

    public long? MilestoneId { get; set; }

    public long? GroupId { get; set; }

    public byte[] ShowMinimap { get; set; }

    public byte[] UseTargetDecal { get; set; }

    public byte[] UseTargetSilhouette { get; set; }

    public byte[] UseTargetHighlight { get; set; }

    public double? TargetDecalSize { get; set; }

    public long? SimRadius { get; set; }

    public byte[] CollideShip { get; set; }

    public byte[] CollideVehicle { get; set; }

    public long? ClimateId { get; set; }

    public byte[] SaveIndun { get; set; }

    public string MarkModel { get; set; }

    public byte[] ForceUpAction { get; set; }

    public byte[] LoadModelFromWorld { get; set; }

    public byte[] Parentable { get; set; }

    public byte[] Childable { get; set; }

    public long? FactionId { get; set; }

    public long? GrowthTime { get; set; }

    public byte[] DespawnOnCollision { get; set; }

    public byte[] NoCollision { get; set; }

    public long? RestrictZoneId { get; set; }

    public byte[] Translate { get; set; }
}
