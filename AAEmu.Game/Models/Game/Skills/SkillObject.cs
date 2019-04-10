using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Skills
{
    public enum SkillObjectType
    {
        None = 0,
        Unk1 = 1,
        Unk2 = 2,
        Unk3 = 3,
        Unk4 = 4,
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
                case SkillObjectType.Unk1: // TODO - Skills bound to portals
                    obj = new SkillObjectUnk1();
                    break;
                case SkillObjectType.Unk2:
                    obj = new SkillObjectUnk2();
                    break;
                case SkillObjectType.Unk3:
                    obj = new SkillObjectUnk3();
                    break;
                case SkillObjectType.Unk4:
                    obj = new SkillObjectUnk4();
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
                default:
                    obj = new SkillObject();
                    break;
            }

            obj.Flag = flag;
            return obj;
        }
    }

    public class SkillObjectUnk1 : SkillObject
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
    
    public class SkillObjectUnk2 : SkillObject
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
    
    public class SkillObjectUnk3 : SkillObject
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
    
    public class SkillObjectUnk4 : SkillObject
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
}
