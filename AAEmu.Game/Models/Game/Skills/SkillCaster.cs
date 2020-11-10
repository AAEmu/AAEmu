using System;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Skills
{
    public enum SkillCasterType : byte
    {
        Unit = 0,
        Unk1 = 1, // Doodad
        Item = 2,
        Unk3 = 3, // TODO mountSkillType
        Doodad = 4 // Gimmick
    }

    public abstract class SkillCaster : PacketMarshaler
    {
        public SkillCasterType Type { get; set; }
        public uint ObjId { get; set; }

        public override void Read(PacketStream stream)
        {
            ObjId = stream.ReadBc();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) Type);
            stream.WriteBc(ObjId);
            return stream;
        }

        public static SkillCaster GetByType(SkillCasterType type)
        {
            SkillCaster obj;
            switch (type)
            {
                case SkillCasterType.Unit:
                    obj = new SkillCasterUnit();
                    break;
                case SkillCasterType.Unk1:
                    obj = new SkillCasterUnk1();
                    break;
                case SkillCasterType.Item:
                    obj = new SkillItem();
                    break;
                case SkillCasterType.Unk3:
                    obj = new SkillCasterUnk3();
                    break;
                case SkillCasterType.Doodad:
                    obj = new SkillDoodad();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            obj.Type = type;
            return obj;
        }
    }

    public class SkillCasterUnit : SkillCaster
    {
        public SkillCasterUnit()
        {
        }

        public SkillCasterUnit(uint objId)
        {
            Type = SkillCasterType.Unit;
            ObjId = objId;
        }
    }

    public class SkillCasterUnk1 : SkillCaster
    {
        public SkillCasterUnk1()
        {
        }

        public SkillCasterUnk1(uint objId)
        {
            Type = SkillCasterType.Unk1;
            ObjId = objId;
        }
    }

    public class SkillItem : SkillCaster
    {
        public ulong ItemId { get; set; }
        public uint ItemTemplateId { get; set; }
        public byte Type1 { get; set; }
        public uint Type2 { get; set; }

        public SkillItem()
        {
        }

        public SkillItem(uint objId, ulong itemId, uint itemTemplateId)
        {
            Type = SkillCasterType.Item;
            ObjId = objId;
            ItemId = itemId;
            ItemTemplateId = itemTemplateId;
        }

        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            ItemId = stream.ReadUInt64();
            ItemTemplateId = stream.ReadUInt32();
            Type1 = stream.ReadByte();
            Type2 = stream.ReadUInt32();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(ItemId);
            stream.Write(ItemTemplateId);
            stream.Write(Type1);
            stream.Write(Type2);
            return stream;
        }
    }

    public class SkillCasterUnk3 : SkillCaster
    {
        public uint MountSkillTemplateId { get; set; }

        public SkillCasterUnk3()
        {
        }

        public SkillCasterUnk3(uint objId)
        {
            Type = SkillCasterType.Unk3;
            ObjId = objId;
        }

        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            MountSkillTemplateId = stream.ReadUInt32();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(MountSkillTemplateId);
            return stream;
        }
    }

    public class SkillDoodad : SkillCaster
    {
        public SkillDoodad()
        {
        }

        public SkillDoodad(uint objId)
        {
            Type = SkillCasterType.Doodad;
            ObjId = objId;
        }
    }
}
