namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// A row of <c>slave_collision_damages</c>, referenced by <c>slaves.slave_collision_damage_id</c>.
/// Gains scale the collision formula per struck face, limits cap the resulting damage (0 = no cap).
/// The small sailing ship (id 2) is front 0.5 / side 1.0 / rear 0.8 / bottom 240 / top 2.0 —
/// running aground is what actually wrecks a hull.
/// </summary>
public class SlaveCollisionDamageDesc
{
    public uint Id { get; init; }
    public float FrontGain { get; init; } = 1f;
    public float SideGain { get; init; } = 1f;
    public float RearGain { get; init; } = 1f;
    public float BottomGain { get; init; } = 1f;
    public float TopGain { get; init; } = 1f;
    public int FrontLimit { get; init; }
    public int SideLimit { get; init; }
    public int RearLimit { get; init; }
    public int BottomLimit { get; init; }
    public int TopLimit { get; init; }

    public float GainFor(SlaveCollisionPart part) => part switch
    {
        SlaveCollisionPart.Front => FrontGain,
        SlaveCollisionPart.Rear => RearGain,
        SlaveCollisionPart.Bottom => BottomGain,
        SlaveCollisionPart.Top => TopGain,
        _ => SideGain
    };

    public int LimitFor(SlaveCollisionPart part) => part switch
    {
        SlaveCollisionPart.Front => FrontLimit,
        SlaveCollisionPart.Rear => RearLimit,
        SlaveCollisionPart.Bottom => BottomLimit,
        SlaveCollisionPart.Top => TopLimit,
        _ => SideLimit
    };
}

/// <summary>
/// Hull face reported in ZWUnitCollision's srcPart/trgPart, and echoed back to the client as
/// SCEnvDamage's trailing <c>p</c>. Zone defaults to <see cref="Side"/> when a unit has no physics
/// proxy, and treats <see cref="Bottom"/> specially when the other side of the contact is terrain.
/// </summary>
public enum SlaveCollisionPart : byte
{
    Front = 0,
    Side = 1,
    Rear = 2,
    Bottom = 3,
    Top = 4
}
