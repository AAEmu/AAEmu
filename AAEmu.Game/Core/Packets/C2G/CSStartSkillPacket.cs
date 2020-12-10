using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartSkillPacket : GamePacket
    {
        public CSStartSkillPacket() : base(CSOffsets.CSStartSkillPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var skillId = stream.ReadUInt32();
            if (skillId == 2 || skillId == 3 || skillId == 4)
                return;

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

            _log.Trace("StartSkill: Id {0}, flag {1}", skillId, flag);
            if (skillCaster is SkillCasterUnit scu)
            {
                var unit = WorldManager.Instance.GetUnit(scu.ObjId);
                if (unit is Character character)
                    _log.Debug("{0} is using skill {1}", character.Name, skillId);
            }

            if (SkillManager.Instance.IsDefaultSkill(skillId) || SkillManager.Instance.IsCommonSkill(skillId) && !(skillCaster is SkillItem))
            {
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO переделать...
                skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
            else if (skillCaster is SkillItem)
            {
                var item = Connection.ActiveChar.Inventory.GetItemById(((SkillItem)skillCaster).ItemId);
                if (item == null || skillId != item.Template.UseSkillId)
                    return;
                Connection.ActiveChar.Quests.OnItemUse(item);
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
                skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
            else if (Connection.ActiveChar.Skills.Skills.ContainsKey(skillId))
            {
                var template = SkillManager.Instance.GetSkillTemplate(skillId);
                var skill = new Skill(template, Connection.ActiveChar);
                skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
            else if (skillId > 0 && Connection.ActiveChar.Skills.IsVariantOfSkill(skillId))
            {
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
                skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
            else
            {
                _log.Warn("StartSkill: Id {0}, undefined use type", skillId);
                //If its a valid skill cast it. This fixes interactions with quest items/doodads.
                var unskill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
                if (unskill != null) unskill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
        }
    }
}
