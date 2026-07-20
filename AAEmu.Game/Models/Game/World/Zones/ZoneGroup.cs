namespace AAEmu.Game.Models.Game.World.Zones;

public class ZoneGroup
{
    public uint Id { get; set; }

public uint SoundPackId { get; set; }

public uint SoundId { get; set; }

public uint ImageMap { get; set; }

public float Height { get; set; }

public uint FactionId { get; set; }

public bool EnablePhysicsCollisionDamage { get; set; }

public string DisplayText { get; set; }
    public string Name { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Hight { get; set; }
    public uint TargetId { get; set; }
    public uint FactionChatRegionId { get; set; }
    public bool PirateDesperado { get; set; }
    public uint FishingSeaLootPackId { get; set; }
    public uint FishingLandLootPackId { get; set; }

    public uint BuffId { get; set; } // TODO: 1.2 BuffId

    public ZoneConflict Conflict { get; set; }
}
