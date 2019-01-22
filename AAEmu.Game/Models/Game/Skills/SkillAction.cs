using System;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Skills
{
    public enum SkillActionType : byte
    {
        Unit = 0,
        Unk1 = 1,
        Item = 2,
        Unk3 = 3,
        Doodad = 4
    }

    public abstract class SkillAction : PacketMarshaler
    {
        public SkillActionType Type { get; set; }
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

        public static SkillAction GetByType(SkillActionType type)
        {
            SkillAction obj = null;
            switch (type)
            {
                case SkillActionType.Unit:
                    obj = new SkillActionUnit();
                    break;
                case SkillActionType.Unk1:
                    obj = new SkillActionUnk1();
                    break;
                case SkillActionType.Item:
                    obj = new SkillItem();
                    break;
                case SkillActionType.Unk3:
                    obj = new SkillActionUnk3();
                    break;
                case SkillActionType.Doodad:
                    obj = new SkillDoodad();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            obj.Type = type;
            return obj;
        }
    }

    public class SkillActionUnit : SkillAction
    {
        public SkillActionUnit()
        {
        }

        public SkillActionUnit(uint objId)
        {
            Type = SkillActionType.Unit;
            ObjId = objId;
        }
    }

    public class SkillActionUnk1 : SkillAction
    {
        public SkillActionUnk1()
        {
        }

        public SkillActionUnk1(uint objId)
        {
            Type = SkillActionType.Unk1;
            ObjId = objId;
        }
    }

    public class SkillItem : SkillAction
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
            Type = SkillActionType.Item;
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

    public class SkillActionUnk3 : SkillAction
    {
        public uint MountSkillTemplateId { get; set; }

        public SkillActionUnk3()
        {
        }

        public SkillActionUnk3(uint objId)
        {
            Type = SkillActionType.Unk3;
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

    public class SkillDoodad : SkillAction
    {
        public SkillDoodad()
        {
        }

        public SkillDoodad(uint objId)
        {
            Type = SkillActionType.Doodad;
            ObjId = objId;
        }
    }
}