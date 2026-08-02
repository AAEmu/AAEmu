using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements;

/// <summary>
/// position, vel.x/y/z u16, rot.x/y/z u16, vehicle.angVel vec3f, vehicle.steering f32,
/// vehicle.throttle u8, vehicle.wheelVelCount u8, then that many vehicle.wheelAngVel f32.
/// </summary>
public class VehicleMoveType : MoveType
{
    private const int MaxWheelAngVel = 0x12;

    public new short RotationX { get; set; }
    public new short RotationY { get; set; }
    public new short RotationZ { get; set; }
    public float AngVelX { get; set; }
    public float AngVelY { get; set; }
    public float AngVelZ { get; set; }
    public float Steering { get; set; }
    public byte Throttle { get; set; }
    public List<float> WheelAngVel { get; set; } = [];

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
        Steering = stream.ReadSingle();
        Throttle = stream.ReadByte();
        var wheelAngs = stream.ReadByte();
        if (wheelAngs > MaxWheelAngVel)
        {
            throw new InvalidDataException(
                $"Vehicle wheel velocity count {wheelAngs} exceeds the native maximum of {MaxWheelAngVel}.");
        }

        for (var i = 0; i < wheelAngs; i++)
        {
            WheelAngVel.Add(stream.ReadSingle());
        }
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

        var wheelCount = Math.Min(WheelAngVel.Count, MaxWheelAngVel);
        stream.Write((byte)wheelCount);
        for (var i = 0; i < wheelCount; i++)
        {
            stream.Write(WheelAngVel[i]);
        }

        return stream;
    }
}
