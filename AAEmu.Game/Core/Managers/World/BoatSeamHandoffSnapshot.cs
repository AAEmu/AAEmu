namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// One immutable Zone-A ship state taken at transfer. Zone B must advance this once to its
/// activation tick; it must not restore the enter coordinates after motion has already been applied.
/// </summary>
public readonly struct BoatSeamHandoffSnapshot
{
    public uint Epoch { get; init; }
    public uint Sequence { get; init; }
    public uint FromZone { get; init; }
    public uint ToZone { get; init; }
    public long TransferTickMs { get; init; }
    public long ActivationTickMs { get; init; }

    /// <summary>Type-4 movement timestamp at transfer. Advanced by the same Δt as position.</summary>
    public uint Time { get; init; }

    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public short RotationX { get; init; }
    public short RotationY { get; init; }
    public short RotationZ { get; init; }

    public short VelX { get; init; }
    public short VelY { get; init; }
    public short VelZ { get; init; }
    public float AccelX { get; init; }
    public float AccelY { get; init; }
    public float AccelZ { get; init; }

    public float AngVelX { get; init; }
    public float AngVelY { get; init; }
    public float AngVelZ { get; init; }

    public sbyte Throttle { get; init; }
    public sbyte Steering { get; init; }
    public byte Rpm { get; init; }
}
