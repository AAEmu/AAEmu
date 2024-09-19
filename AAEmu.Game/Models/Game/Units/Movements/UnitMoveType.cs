using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Models;

namespace AAEmu.Game.Models.Game.Units.Movements;

public class UnitMoveType : MoveType
{
    public sbyte[] DeltaMovement { get; set; }
    public GameStanceType Stance { get; set; }
    public MoveTypeAlertness Alertness { get; set; }
    public byte GcFlags { get; set; }
    public ushort GcPart { get; set; }
    public ushort GcPartId { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public float Z2 { get; set; }
    public sbyte RotationX2 { get; set; }
    public sbyte RotationY2 { get; set; }
    public sbyte RotationZ2 { get; set; }
    public uint ClimbData { get; set; }
    public uint GcId { get; set; }
    public ushort FallVel { get; set; }
    public ushort ActorFlags { get; set; }
    public uint MaxPushedUnitId { get; set; }

    public override void Read(PacketStream stream)
    {
        base.Read(stream);
        (X, Y, Z) = stream.ReadPosition();
        VelX = stream.ReadInt16();
        VelY = stream.ReadInt16();
        VelZ = stream.ReadInt16();
        RotationX = stream.ReadSByte();
        RotationY = stream.ReadSByte();
        RotationZ = stream.ReadSByte();
        DeltaMovement = new sbyte[3];
        DeltaMovement[0] = stream.ReadSByte();
        DeltaMovement[1] = stream.ReadSByte();
        DeltaMovement[2] = stream.ReadSByte();
        Stance = (GameStanceType)stream.ReadSByte();
        Alertness = (MoveTypeAlertness)stream.ReadByte();
        ActorFlags = stream.ReadUInt16(); // ushort in 3.0.3.0, sbyte in 1.2
        if ((short)ActorFlags < 0)
            FallVel = stream.ReadUInt16(); // actor.fallVel
        if ((ActorFlags & 0x20) != 0)
        {
            GcFlags = stream.ReadByte();    // actor.gcFlags
            GcPart = stream.ReadUInt16();   // actor.gcPart
            GcPartId = stream.ReadUInt16(); // actor.gcPartId
            (X2, Y2, Z2) = stream.ReadPosition(); // ix, iy, iz
            RotationX2 = stream.ReadSByte();
            RotationY2 = stream.ReadSByte();
            RotationZ2 = stream.ReadSByte();
        }
        if ((ActorFlags & 0x60) != 0)
            GcId = stream.ReadUInt32();            // actor.gcId
        if ((ActorFlags & 0x40) != 0 || (ActorFlags & 0x8000) != 0)
            ClimbData = stream.ReadUInt32();       // actor.climbData
        if ((short)ActorFlags < 0)
        {
            var type = stream.ReadByte();   // type
            var posx = stream.ReadInt16(); // posx
            var posy = stream.ReadInt16(); // posy
            var posz = stream.ReadInt16(); // posz
            if (type == 1)
            {
                var unk1 = stream.ReadBc(); // unk1
            }
            else if (type == 2)
            {
                var unk3 = stream.ReadBc(); // unk2
            }
            else if (type == 3)
            {
                var unk2 = stream.ReadBc(); // unk3
            }
        }
        if ((ActorFlags & 0x100) != 0)
            MaxPushedUnitId = stream.ReadUInt32(); // actor.maxPushedUnitId
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
        stream.Write(DeltaMovement[0]);
        stream.Write(DeltaMovement[1]);
        stream.Write(DeltaMovement[2]);
        stream.Write((byte)Stance);
        stream.Write((byte)Alertness);
        stream.Write(ActorFlags);
        if ((short)ActorFlags < 0)
            stream.Write(FallVel);
        if ((ActorFlags & 0x20) != 0)
        {
            stream.Write(GcFlags);
            stream.Write(GcPart);
            stream.Write(GcPartId);
            stream.WritePosition(X2, Y2, Z2);
            stream.Write(RotationX2);
            stream.Write(RotationY2);
            stream.Write(RotationZ2);
        }
        if ((ActorFlags & 0x60) != 0)
            stream.Write(GcId);
        if ((ActorFlags & 0x40) != 0 || (ActorFlags & 0x8000) != 0)
            stream.Write(ClimbData);
        if ((short)ActorFlags < 0)
        {
            var type = 0;
            stream.Write((byte)0); // type
            stream.Write((short)0); // posx
            stream.Write((short)0); // posy
            stream.Write((short)0); // posz
            if (type == 1)
            {
                stream.WriteBc(0u); // unk1
            }
            else if (type == 2)
            {
                stream.WriteBc(0u); // unk2
            }
            else if (type == 3)
            {
                stream.WriteBc(0u); // unk3
            }
        }
        if ((ActorFlags & 0x100) != 0)
            stream.Write(MaxPushedUnitId);
        return stream;
    }
}
