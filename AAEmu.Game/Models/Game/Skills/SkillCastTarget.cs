using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Skills;

public enum SkillCastTargetType : byte
{
    Unit = 0,
    Position = 1,
    Position2 = 2,
    Item = 3,
    Doodad = 4,
    Position3 = 5
}

public abstract class SkillCastTarget : PacketMarshaler
{
    public SkillCastTargetType Type { get; set; }
    public uint ObjId { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)Type);
        return stream;
    }

    public static SkillCastTarget GetByType(SkillCastTargetType type)
    {
        SkillCastTarget obj;
        switch (type)
        {
            case SkillCastTargetType.Unit:
                obj = new SkillCastUnitTarget();
                break;
            case SkillCastTargetType.Position:
                obj = new SkillCastPositionTarget();
                break;
            case SkillCastTargetType.Position2:
                obj = new SkillCastPosition2Target();
                break;
            case SkillCastTargetType.Item:
                obj = new SkillCastItemTarget();
                break;
            case SkillCastTargetType.Doodad:
                obj = new SkillCastDoodadTarget();
                break;
            case SkillCastTargetType.Position3:
                obj = new SkillCastPosition3Target();
                break;
            default:
                //throw new ArgumentOutOfRangeException(nameof(type), type, null);
                obj = new SkillCastUnitTarget();
                Logger.Fatal(new ArgumentOutOfRangeException(nameof(type), type, null));
                break;
        }

        obj.Type = type;
        return obj;
    }
}

public class SkillCastUnitTarget : SkillCastTarget
{
    public SkillCastUnitTarget()
    {
    }

    public SkillCastUnitTarget(uint objId)
    {
        ObjId = objId;
    }

    public override void Read(PacketStream stream)
    {
        ObjId = stream.ReadBc();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.WriteBc(ObjId);
        return stream;
    }
}

public class SkillCastPositionTarget : SkillCastTarget
{
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float PosRot { get; set; }
    public uint ObjId1 { get; set; }
    public uint ObjId2 { get; set; }
    public uint ObjId3 { get; set; } // add in 3+

    public override void Read(PacketStream stream)
    {
        PosX = Helpers.ConvertLongX(stream.ReadInt64());
        PosY = Helpers.ConvertLongY(stream.ReadInt64());
        PosZ = stream.ReadSingle();
        PosRot = stream.ReadSingle();
        ObjId1 = stream.ReadBc();
        ObjId2 = stream.ReadBc();
        ObjId3 = stream.ReadBc();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write(Helpers.ConvertLongX(PosX));
        stream.Write(Helpers.ConvertLongY(PosY));
        stream.Write(PosZ);
        stream.Write(PosRot);
        stream.WriteBc(ObjId1);
        stream.WriteBc(ObjId2);
        stream.WriteBc(ObjId3);
        return stream;
    }
}

public class SkillCastPosition2Target : SkillCastTarget
{
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float EndPosX { get; set; }
    public float EndPosY { get; set; }
    public float EndPosZ { get; set; }
    public float NormX { get; set; }
    public float NormY { get; set; }
    public float NormZ { get; set; }

    public override void Read(PacketStream stream)
    {
        PosX = Helpers.ConvertLongX(stream.ReadInt64());
        PosY = Helpers.ConvertLongY(stream.ReadInt64());
        PosZ = stream.ReadSingle();

        EndPosX = stream.ReadSingle();
        EndPosY = stream.ReadSingle();
        EndPosZ = stream.ReadSingle();

        NormX = stream.ReadSingle();
        NormY = stream.ReadSingle();
        NormZ = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write(Helpers.ConvertLongX(PosX));
        stream.Write(Helpers.ConvertLongY(PosY));
        stream.Write(PosZ);

        stream.Write(EndPosX);
        stream.Write(EndPosY);
        stream.Write(EndPosZ);

        stream.Write(NormX);
        stream.Write(NormY);
        stream.Write(NormZ);
        return stream;
    }
}

public class SkillCastItemTarget : SkillCastTarget
{
    public ulong Id { get; set; }
    public uint Type1 { get; set; }
    public byte Type2 { get; set; }

    public override void Read(PacketStream stream)
    {
        ObjId = stream.ReadBc();
        Id = stream.ReadUInt64();
        Type1 = stream.ReadUInt32();
        Type2 = stream.ReadByte();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.WriteBc(ObjId);
        stream.Write(Id);
        stream.Write(Type1);
        stream.Write(Type2);

        return stream;
    }
}

public class SkillCastDoodadTarget : SkillCastTarget
{
    public override void Read(PacketStream stream)
    {
        ObjId = stream.ReadBc();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.WriteBc(ObjId);
        return stream;
    }
}

public class SkillCastPosition3Target : SkillCastTarget
{
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float Pitch { get; set; }

    public override void Read(PacketStream stream)
    {
        PosX = Helpers.ConvertLongX(stream.ReadInt64());
        PosY = Helpers.ConvertLongY(stream.ReadInt64());
        PosZ = stream.ReadSingle();
        Pitch = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write(Helpers.ConvertLongX(PosX));
        stream.Write(Helpers.ConvertLongY(PosY));
        stream.Write(PosZ);
        stream.Write(Pitch);
        return stream;
    }
}
