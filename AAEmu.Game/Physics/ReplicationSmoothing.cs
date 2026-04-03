#nullable enable

using AAEmu.Game.Models.Game.Units;

using Jitter2.Dynamics;

namespace AAEmu.Game.Physics;

/// <summary>
/// Smoothed position, linear velocity, turn bank, and shore trim for packets.
/// Hull yaw and physics euler from <see cref="RigidBody"/> stay unsmoothed in replication (no yaw smoothing).
/// </summary>
public sealed class ReplicationSmoothing
{
    public bool Seeded { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float VelPx { get; set; }
    public float VelPy { get; set; }
    public float VelPz { get; set; }
    /// <summary>Second low-pass on turn bank for replication (softer λ than vertical; targets <see cref="Slave.BankAngle"/>).</summary>
    public float BankSmoothed { get; set; }
    /// <summary>Second low-pass on shore pitch delta for replication (targets <see cref="Slave.GroundPitchAngle"/>).</summary>
    public float GroundPitchSmoothed { get; set; }
    /// <summary>Remaining movement broadcasts with stronger smoothing after hull–hull resolution.</summary>
    public byte ContactHoldTicks { get; set; }

    public void Reset()
    {
        Seeded = false;
        ContactHoldTicks = 0;
    }
}
