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
            // -------------------------------
            var typeActionFirst = stream.ReadByte();
            var actionFirst = SkillAction.GetByType((SkillActionType) typeActionFirst);
            actionFirst.Read(stream); // кто применяет
            var typeActionSecond = stream.ReadByte();
            var actionSecond = SkillAction.GetByType((SkillActionType) typeActionSecond);
            actionSecond.Read(stream); // на кого применяют
            // -------------------------------
            // TODO WTF?!?!
            // -------------------------------
            // var flag = stream.ReadByte();

            _log.Debug("StartSkill: Id {0}", skillId);

            if (SkillManager.Instance.IsDefaultSkill(skillId) || SkillManager.Instance.IsCommonSkill(skillId))
            {
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO переделать...
                skill.Use(Connection.ActiveChar, actionFirst, actionSecond);
            }
            else if (actionFirst is SkillItem)
            {
                var item = Connection.ActiveChar.Inventory.GetItem(((SkillItem) actionFirst).ItemId);
                if (item == null || skillId != item.Template.UseSkillId)
                    return;
                // TODO Quest OnItemUse
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
                skill.Use(Connection.ActiveChar, actionFirst, actionSecond);
            }
            else if (Connection.ActiveChar.Skills.Skills.ContainsKey(skillId))
            {
                var skill = Connection.ActiveChar.Skills.Skills[skillId];
                skill.Use(Connection.ActiveChar, actionFirst, actionSecond);
            }
        }
    }
}