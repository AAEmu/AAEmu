using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartSkillPacket : GamePacket
    {
        public CSStartSkillPacket() : base(0x052, 1)
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
            var skillObject = SkillObject.GetByType((SkillObjectType)flagType);
            if (flagType > 0) skillObject.Read(stream);

            _log.Debug("StartSkill: Id {0}, flag {1}", skillId, flag);

            if (SkillManager.Instance.IsDefaultSkill(skillId) || SkillManager.Instance.IsCommonSkill(skillId) && !(skillCaster is SkillItem))
            {
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO переделать...
                skill.Use(DbLoggerCategory.Database.Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
            else if (skillCaster is SkillItem)
            {
                var item = DbLoggerCategory.Database.Connection.ActiveChar.Inventory.GetItemById(((SkillItem)skillCaster).ItemId);
                if (item == null || skillId != item.Template.UseSkillId)
                    return;
                DbLoggerCategory.Database.Connection.ActiveChar.Quests.OnItemUse(item);
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
                skill.Use(DbLoggerCategory.Database.Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
            else if (DbLoggerCategory.Database.Connection.ActiveChar.Skills.Skills.ContainsKey(skillId))
            {
                var skill = DbLoggerCategory.Database.Connection.ActiveChar.Skills.Skills[skillId];
                skill.Use(DbLoggerCategory.Database.Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
            else if (skillId > 0 && DbLoggerCategory.Database.Connection.ActiveChar.Skills.IsVariantOfSkill(skillId))
            {
                var skill = DbLoggerCategory.Database.Connection.ActiveChar.Skills.Skills[skillId];
                skill.Use(DbLoggerCategory.Database.Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
            else
                _log.Warn("StartSkill: Id {0}, undefined use type", skillId);
        }
    }
}
