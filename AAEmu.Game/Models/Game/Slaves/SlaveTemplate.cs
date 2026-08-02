using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Slaves;

public class SlaveTemplate
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint ModelId { get; set; }
    public bool Mountable { get; set; }
    public float SpawnXOffset { get; set; }
    public float SpawnYOffset { get; set; }
    public FactionsEnum FactionId { get; set; }
    public uint Level { get; set; }
    public int Cost { get; set; }
    public SlaveKind SlaveKind { get; set; }
    public uint SpawnValidAreaRance { get; set; }
    public uint SlaveInitialItemPackId { get; set; }
    public uint SlaveCustomizingId { get; set; }
    public bool Customizable { get; set; }
    /// <summary>slaves.portal_time — how long the summon/dismiss portal runs, in seconds.</summary>
    public float PortalTime { get; set; }

    /// <summary>slaves.portal_spawn_fx_id — fx_groups entry played as the hull arrives (0 = none).</summary>
    public uint PortalSpawnFxId { get; set; }

    /// <summary>slaves.portal_despawn_fx_id — fx_groups entry played as the hull is dismissed (0 = none).</summary>
    public uint PortalDespawnFxId { get; set; }

    /// <summary>slaves.portal_scale — size the portal effect is authored at (e.g. 7.5 for the clipper).</summary>
    public float PortalScale { get; set; }
    /// <summary>slaves.slave_collision_damage_id — row of per-face collision gains/limits (0 = none).</summary>
    public uint SlaveCollisionDamageId { get; set; }
    public int Hp25DoodadCount { get; set; }
    public int Hp50DoodadCount { get; set; }
    public int Hp75DoodadCount { get; set; }

    public List<SlaveInitialBuffs> InitialBuffs { get; } = [];
    public List<SlavePassiveBuffs> PassiveBuffs { get; } = [];
    public List<SlaveDoodadBindings> DoodadBindings { get; } = [];
    public List<SlaveDoodadBindings> HealingPointDoodads { get; } = [];
    public List<SlaveBindings> SlaveBindings { get; } = [];
    public List<SlaveDropDoodad> SlaveDropDoodads { get; } = [];
    public List<BonusTemplate> Bonuses { get; set; } = [];

    public bool IsABoat()
    {
        return SlaveKind == SlaveKind.Boat || SlaveKind == SlaveKind.Fishboat ||
               SlaveKind == SlaveKind.Speedboat || SlaveKind == SlaveKind.MerchantShip ||
               SlaveKind == SlaveKind.BigSailingShip || SlaveKind == SlaveKind.SmallSailingShip;
    }

    public bool IsClientDrivenLandVehicle()
    {
        return SlaveKind is SlaveKind.Tank or SlaveKind.Machine or SlaveKind.SiegeWeapon;
    }
}
