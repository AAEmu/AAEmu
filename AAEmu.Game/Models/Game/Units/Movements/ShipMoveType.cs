using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements;

/// <summary>
/// position, vel.x/y/z u16, rot.x/y/z u16, ship.angVel vec3f, ship.steering u8,
/// ship.throttle u8, ship.rpm u8, ship.zoneId i16, ship.stucked bool.
/// </summary>
public class ShipMoveType : MoveType
{
    /// <summary>
    /// Metres per second spanned by the full extent of the quantised velocity fields. Velocity travels
    /// as three 16-bit values scaled against this, so the body cannot express more and saturates at
    /// the limit rather than reporting the real figure.
    /// </summary>
    public const float VelocityQuantizationScale = 30f;

    /// <summary>
    /// The speed the simulator itself reports, in metres per second, decoded from the quantised
    /// velocity fields. This is what the hull's physics believes it is doing, as opposed to the speed
    /// inferred from successive positions — the two disagreeing is how an impulse that adds to the
    /// hull's existing motion is told apart from one that replaces it.
    /// </summary>
    public float ReportedSpeed =>
        MathF.Sqrt(((float)VelX * VelX) + ((float)VelY * VelY) + ((float)VelZ * VelZ))
        / short.MaxValue * VelocityQuantizationScale;

    public new short RotationX { get; set; }
    public new short RotationY { get; set; }
    public new short RotationZ { get; set; }
    public float AngVelX { get; set; }
    public float AngVelY { get; set; }
    public float AngVelZ { get; set; }
    public sbyte Steering { get; set; }
    public sbyte Throttle { get; set; }
    /// <summary>Engine rpm, stored at +0x122 between throttle and zoneId; absent in the v1.2 layout.</summary>
    public byte Rpm { get; set; }
    public ushort ZoneId { get; set; }
    public bool Stuck { get; set; }

    public override void Read(PacketStream stream)
    {
        base.Read(stream);
        (X, Y, Z) = stream.ReadPosition();
        VelX = stream.ReadInt16();
        VelY = stream.ReadInt16();
        VelZ = stream.ReadInt16();
        RotationX = stream.ReadInt16();
        RotationY = stream.ReadInt16();
        RotationZ = stream.ReadInt16();

        AngVelX = stream.ReadSingle();
        AngVelY = stream.ReadSingle();
        AngVelZ = stream.ReadSingle();
        Steering = stream.ReadSByte();
        Throttle = stream.ReadSByte();
        Rpm = stream.ReadByte();
        ZoneId = stream.ReadUInt16();
        Stuck = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.WritePosition(X, Y, Z);

        stream.Write(VelX);
        stream.Write(VelY);
        stream.Write(VelZ);

        stream.Write(RotationX);
        stream.Write(RotationY);
        stream.Write(RotationZ);

        stream.Write(AngVelX);
        stream.Write(AngVelY);
        stream.Write(AngVelZ);

        stream.Write(Steering);
        stream.Write(Throttle);
        stream.Write(Rpm);

        stream.Write(ZoneId);
        stream.Write(Stuck);

        return stream;
    }

    public void UseSlaveBase(Slave slave)
    {
        X = slave.Transform.World.Position.X;
        Y = slave.Transform.World.Position.Y;
        Z = slave.Transform.World.Position.Z;
        (RotationX, RotationY, RotationZ) = slave.Transform.World.ToRollPitchYawShorts();
        VelX = 0;
        VelY = 0;
        VelZ = 0;
        AngVelX = 0;
        AngVelY = 0;
        AngVelZ = 0;
        ZoneId = (ushort)slave.Transform.ZoneId;
        Time = (uint)(DateTime.UtcNow - slave.SpawnTime).TotalMilliseconds;
        Stuck = false;
        // Must match physics: smoothed values. Request jumps with each client packet and makes the rudder stutter.
        Throttle = slave.Throttle;
        Steering = slave.Steering;
    }
}
