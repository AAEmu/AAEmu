using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Skills;

public enum SkillObjectType
{
    None = 0,
    PortalInfo = 1,
    SavePortalInfo = 2,
    Text = 3,
    Position = 4,
    Unk5 = 5,
    Unk6 = 6,
    ItemGradeEnchantingSupport = 7
}

public class SkillObject : PacketMarshaler
{
    public SkillObjectType Flag { get; set; } = SkillObjectType.None;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)Flag);
        return stream;
    }

    public static SkillObject GetByType(SkillObjectType flag)
    {
        SkillObject obj;
        switch (flag)
        {
            case SkillObjectType.PortalInfo: // TODO - Skills bound to portals
                obj = new SkillObjectPortalInfo();
                break;
            case SkillObjectType.SavePortalInfo: // TODO - Skills bound to home portals
                obj = new SkillObjectSavePortalInfo();
                break;
            case SkillObjectType.Text: // Used by BotReport
                obj = new SkillObjectText();
                break;
            case SkillObjectType.Position:
                obj = new SkillObjectPosition();
                break;
            case SkillObjectType.Unk5:
                obj = new SkillObjectUnk5();
                break;
            case SkillObjectType.Unk6:
                obj = new SkillObjectUnk6();
                break;
            case SkillObjectType.ItemGradeEnchantingSupport:
                obj = new SkillObjectItemGradeEnchantingSupport();
                break;
            case SkillObjectType.None:
            default:
                obj = new SkillObject();
                break;
        }

        obj.Flag = flag;
        return obj;
    }
}

public class SkillObjectPortalInfo : SkillObject
{
    public byte Type { get; set; }
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadByte();
        Id = stream.ReadInt32();
        X = Helpers.ConvertLongX(stream.ReadInt64());
        Y = Helpers.ConvertLongX(stream.ReadInt64());
        Z = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Type);
        stream.Write(Id);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongX(Y));
        stream.Write(Z);
        return stream;
    }
}

public class SkillObjectSavePortalInfo : SkillObject
{
    public int Id { get; set; }
    public string Name { get; set; }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadInt32();
        Name = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Id);
        stream.Write(Name);
        return stream;
    }
}

public class SkillObjectText : SkillObject
{
    public string Msg { get; set; }

    public override void Read(PacketStream stream)
    {
        Msg = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Msg);
        return stream;
    }
}

public class SkillObjectPosition : SkillObject
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public override void Read(PacketStream stream)
    {
        X = Helpers.ConvertLongX(stream.ReadInt64());
        Y = Helpers.ConvertLongY(stream.ReadInt64());
        Z = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongY(Y));
        stream.Write(Z);
        return stream;
    }
}

public class SkillObjectUnk5 : SkillObject
{
    public int Step { get; set; }

    public override void Read(PacketStream stream)
    {
        Step = stream.ReadInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Step);
        return stream;
    }
}

public class SkillObjectUnk6 : SkillObject
{
    public string Name { get; set; }

    public override void Read(PacketStream stream)
    {
        Name = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Name);
        return stream;
    }
}

public class SkillObjectItemGradeEnchantingSupport : SkillObject
{
    public uint Id { get; set; }
    public ulong SupportItemId { get; set; }
    public bool AutoUseAaPoint { get; set; }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadUInt32();
        SupportItemId = stream.ReadUInt64();
        AutoUseAaPoint = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Id);
        stream.Write(SupportItemId);
        stream.Write(AutoUseAaPoint);
        return stream;
    }
}
