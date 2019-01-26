using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartSkillPacket : GamePacket
    {
        public CSStartSkillPacket() : base(0x050, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var skillId = stream.ReadUInt32();

            var skillCasterType = stream.ReadByte(); // кто применяет
            var skillCaster = SkillCaster.GetByType((SkillCasterType)skillCasterType);
            skillCaster.Read(stream);

            var skillCastTargetType = stream.ReadByte(); // на кого применяют
            var skillCastTarget = SkillCastTarget.GetByType((SkillCastTargetType)skillCastTargetType);
            skillCastTarget.Read(stream);

            var flag = stream.ReadByte();
            var flagType = flag & 15;
            switch (flagType)
            {
                case 1:
                    stream.ReadByte(); // type
                    stream.ReadInt32(); // id
                    break;
                case 2:
                    stream.ReadInt32(); // id
                    stream.ReadString(); // name // TODO max 128
                    break;
                case 3:
                    stream.ReadString(); // msg // TODO max 200
                    break;
                case 4:
                    stream.ReadInt64(); // id
                    stream.ReadInt64(); // y
                    stream.ReadSingle(); // z
                    break;
                case 5:
                    stream.ReadInt32(); // step
                    break;
                case 6:
                    stream.ReadString(); // name // TODO max 128
                    break;
            }

            _log.Debug("StartSkill: Id {0}, flag {1}", skillId, flag);

            if (SkillManager.Instance.IsDefaultSkill(skillId) || SkillManager.Instance.IsCommonSkill(skillId))
            {
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO переделать...
                skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget);
            }
            else if (skillCaster is SkillItem)
            {
                var item = Connection.ActiveChar.Inventory.GetItem(((SkillItem)skillCaster).ItemId);
                if (item == null || skillId != item.Template.UseSkillId)
                    return;
                // TODO Quest OnItemUse
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
                skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget);
            }
            else if (Connection.ActiveChar.Skills.Skills.ContainsKey(skillId))
            {
                var skill = Connection.ActiveChar.Skills.Skills[skillId];
                skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget);
            }
        }
    }
}
