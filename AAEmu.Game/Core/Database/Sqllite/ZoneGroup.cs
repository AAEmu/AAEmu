using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ZoneGroup
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    public double? W { get; set; }

    public double? H { get; set; }

    public long? ImageMap { get; set; }

    public long? SoundId { get; set; }

    public long? TargetId { get; set; }

    public string DisplayText { get; set; }

    public long? FactionChatRegionId { get; set; }

    public long? SoundPackId { get; set; }

    public byte[] PirateDesperado { get; set; }

    public long? FishingSeaLootPackId { get; set; }

    public long? FishingLandLootPackId { get; set; }

    public long? BuffId { get; set; }
}
