using System;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartSkillPacket : GamePacket
    {
        public CSStartSkillPacket() : base(CSOffsets.CSStartSkillPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Will delay for 150 Milliseconds to eliminate the hanging of the skill
            var source = new CancellationTokenSource();
            var t = Task.Run(async delegate
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), source.Token);
                return 0;
            });
            try {
                t.Wait();
            }
            catch (AggregateException ae) {
                foreach (var e in ae.InnerExceptions)
                    _log.Trace("{0}: {1}", e.GetType().Name, e.Message);
            }

            var skillId = stream.ReadUInt32();
            // if (skillId == 2 || skillId == 3 || skillId == 4)
            //     return;

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
                {
                    _log.Debug("{0} is using skill {1}", character.Name, skillId);
                }
            }

            if (skillCaster is SkillCasterMount)
            {
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
                var mate = MateManager.Instance.GetActiveMate(Connection.ActiveChar.ObjId);
                var slave = SlaveManager.Instance.GetActiveSlaveByOwnerObjId(Connection.ActiveChar.ObjId);
                var mountAttachedSkill = MateManager.Instance.GetMountAttachedSkills(skillId);

                if (mate != null && Connection.ActiveChar.IsRiding)
                    skill.Use(mate, skillCaster, skillCastTarget, skillObject);
                else
                    skill.Use(slave, skillCaster, skillCastTarget, skillObject);

                if (mountAttachedSkill == 0) { return; }

                var trg = Connection.ActiveChar.CurrentTarget;

                if (trg == null)
                    Connection.ActiveChar.UseSkill(mountAttachedSkill, Connection.ActiveChar);
                else
                    Connection.ActiveChar.UseSkill(mountAttachedSkill, (IUnit)trg);
            }
            else if (SkillManager.Instance.IsDefaultSkill(skillId) || SkillManager.Instance.IsCommonSkill(skillId) && !(skillCaster is SkillItem))
            {
                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO: переделать / rewrite ...
                skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
            else if (skillCaster is SkillItem)
            {
                var item = Connection.ActiveChar.Inventory.GetItemById(((SkillItem)skillCaster).ItemId);
                // добавил проверку на ItemBindType.BindOnPickup для записи портала с помощью камина в доме
                if (item == null || skillId != item.Template.UseSkillId && item.Template.BindType != ItemBindType.BindOnPickup)
                    return;
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
                if (unskill != null)
                    unskill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
            }
        }
    }
}
