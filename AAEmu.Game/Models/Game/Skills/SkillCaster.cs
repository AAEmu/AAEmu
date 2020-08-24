using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Skills
{
    public abstract class SkillCaster : PacketMarshaler
    {
        public EffectOriginType Type { get; set; }
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

        public static SkillCaster GetByType(EffectOriginType type)
        {
            SkillCaster obj;
            switch (type)
            {
                case EffectOriginType.Skill:
                    obj = new SkillCasterUnit();
                    break;
                case EffectOriginType.Plot:
                    obj = new CasterEffectPlot();
                    break;
                case EffectOriginType.Buff:
                    obj = new CasterEffectBuff();
                    break;
                case EffectOriginType.Passive:
                    obj = new CasterEffectPassive();
                    break;
                case EffectOriginType.Doodad:
                    obj = new CasterEffectDoodad();
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
            Type = EffectOriginType.Skill;
            ObjId = objId;
        }
    }

    public class CasterEffectPlot : SkillCaster
    {
        public CasterEffectPlot()
        {
        }

        public CasterEffectPlot(uint objId)
        {
            Type = EffectOriginType.Plot;
            ObjId = objId;
        }
    }

    public class CasterEffectBuff : SkillCaster
    {
        public ulong ItemId { get; set; }
        public uint ItemTemplateId { get; set; }
        public byte Type1 { get; set; }
        public uint Type2 { get; set; }

        public CasterEffectBuff()
        {
        }

        public CasterEffectBuff(uint objId, ulong itemId, uint itemTemplateId)
        {
            Type = EffectOriginType.Buff;
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

    public class CasterEffectPassive : SkillCaster
    {
        public uint MountSkillTemplateId { get; set; }

        public CasterEffectPassive()
        {
        }

        public CasterEffectPassive(uint objId)
        {
            Type = EffectOriginType.Passive;
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

    public class CasterEffectDoodad : SkillCaster
    {
        public CasterEffectDoodad()
        {
        }

        public CasterEffectDoodad(uint objId)
        {
            Type = EffectOriginType.Doodad;
            ObjId = objId;
        }
    }
}
