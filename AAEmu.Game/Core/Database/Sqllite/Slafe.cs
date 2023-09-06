using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Slafe
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ModelId { get; set; }

    public byte[] Mountable { get; set; }

    public double? OffsetX { get; set; }

    public double? OffsetY { get; set; }

    public double? OffsetZ { get; set; }

    public double? ObbPosX { get; set; }

    public double? ObbPosY { get; set; }

    public double? ObbPosZ { get; set; }

    public double? ObbSizeX { get; set; }

    public double? ObbSizeY { get; set; }

    public double? ObbSizeZ { get; set; }

    public long? PortalSpawnFxId { get; set; }

    public double? PortalScale { get; set; }

    public double? PortalTime { get; set; }

    public long? PortalDespawnFxId { get; set; }

    public long? Hp25DoodadCount { get; set; }

    public long? Hp50DoodadCount { get; set; }

    public long? Hp75DoodadCount { get; set; }

    public double? SpawnXOffset { get; set; }

    public double? SpawnYOffset { get; set; }

    public long? FactionId { get; set; }

    public long? Level { get; set; }

    public long? Cost { get; set; }

    public long? SlaveKindId { get; set; }

    public long? SpawnValidAreaRange { get; set; }

    public long? SlaveInitialItemPackId { get; set; }

    public long? SlaveCustomizingId { get; set; }

    public byte[] Customizable { get; set; }
}
