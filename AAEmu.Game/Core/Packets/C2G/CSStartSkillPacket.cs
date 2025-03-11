using System;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartSkillPacket : GamePacket
{
    public CSStartSkillPacket() : base(CSOffsets.CSStartSkillPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Will delay for 150 Milliseconds to eliminate the hanging of the skill
        using var source = new CancellationTokenSource();
        var t = Task.Run(async delegate
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), source.Token);
            return 0;
        });
        try
        {
            t.Wait();
        }
        catch (AggregateException ae)
        {
            foreach (var e in ae.InnerExceptions)
                Logger.Trace("{0}: {1}", e.GetType().Name, e.Message);
        }

        var skillId = stream.ReadUInt32();

        var skillCasterType = stream.ReadByte(); // кто применяет
        var skillCaster = SkillCaster.GetByType((SkillCasterType)skillCasterType);
        skillCaster.Read(stream);

        var skillCastTargetType = stream.ReadByte(); // на кого применяют
        var skillCastTarget = SkillCastTarget.GetByType((SkillCastTargetType)skillCastTargetType);
        skillCastTarget.Read(stream);

        var flag = stream.ReadByte();
        var flagType = flag & 63; // in 1.2 = 15, in 3+ = 63
        var skillObject = SkillObject.GetByType((SkillObjectType)flagType);
        if (flagType > 0) skillObject.Read(stream);

        Logger.Info($"StartSkill: Id {skillId}, flag {flag}, caster={skillCaster.ObjId}, target={skillCastTarget.ObjId}");

        var skillResult = SkillResult.Success;
        var skillResultErrorValue = 0u;
        Skill skill = null;

        if (skillCaster is SkillCasterUnit scu)
        {
            var unit = WorldManager.Instance.GetUnit(scu.ObjId);
            if (unit is Character character)
                Logger.Info($"{character.Name}:{character.ObjId} is using skill={skillId}");
        }

        if (skillCaster is SkillCasterMount scm)
        {
            // Mount or Slave skill
            Logger.Trace($"SkillCasterMount - MountSkillTemplateId {scm.MountSkillTemplateId}");
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));

            var caster = WorldManager.Instance.GetBaseUnit(skillCaster.ObjId);
            var mate = caster as Mate;
            var slave = caster as Slave;
            var mountAttachedSkill = 0u;

            if ((mate != null) || (slave != null))
            {
                // check if it's a mate or slave skill and return it's rider/operator related skill
                mountAttachedSkill = MateManager.Instance.GetMountAttachedSkills(skillId, Connection.ActiveChar.AttachedPoint);
            }

            // Use the main skill on the mate/slave
            if (skill.Use(caster, skillCaster, skillCastTarget, skillObject, false, out skillResultErrorValue) != SkillResult.Success)
            {
                // skill.Stop(caster, null, skillCaster);
            }

            // If no rider/operator skill is linked, we can stop here
            if (mountAttachedSkill == 0)
                return;

            // Use player's currently selected for the rider/operator skill
            var riderTarget = Connection.ActiveChar.CurrentTarget as Unit;

            // Execute the rider/operator skill as the player using either target or self
            skillResult = Connection.ActiveChar.UseSkill(mountAttachedSkill, riderTarget ?? Connection.ActiveChar);
        }
        //else if (Connection.ActiveChar.IsAutoAttack && skillId == Connection.ActiveChar.AutoAttackTask?.Skill?.Template?.Id)
        //{
        //    // Same as already executing auto-skill, just send the success result.
        //    skill = Connection.ActiveChar.AutoAttackTask.Skill;
        //    skillResult = SkillResult.Success;
        //}
        else if (SkillManager.Instance.IsDefaultSkill(skillId) || SkillManager.Instance.IsCommonSkill(skillId) && !(skillCaster is SkillItem))
        {
            // Is it a common skill?
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO: переделать / rewrite ...
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject, false, out skillResultErrorValue);
            //if ((skillResult == SkillResult.Success) && (skillId < 5000) && (skillCaster.ObjId == Connection.ActiveChar.ObjId))
            //{
            //    // All basic combat skills are below ID 5000, only 2 (melee),3 (offhand) and 4 (ranged) exist, next actual skill used is 5001
            //    Connection.ActiveChar.IsAutoAttack = true;
            //    Connection.ActiveChar.StartAutoSkill(skill);
            //}
        }
        else if (skillCaster is SkillItem si)
        {
            // A skill triggered by a item
            var player = Connection.ActiveChar;
            // var item = player.Inventory.GetItemById(si.ItemId);
            // добавил проверку на ItemBindType.BindOnPickup для записи портала с помощью камина в доме
            if (si.SkillSourceItem == null || skillId != si.SkillSourceItem.Template.UseSkillId && si.SkillSourceItem.Template.BindType != ItemBindType.BindOnPickup)
                return;
            // si.ItemTemplateId = item.TemplateId;
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skillResult = skill.Use(player, skillCaster, skillCastTarget, skillObject, false, out skillResultErrorValue);
        }
        else if (Connection.ActiveChar.Skills.Skills.ContainsKey(skillId))
        {
            // Is it one of our learned character skills?
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            skill = new Skill(template, Connection.ActiveChar);
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject, false, out skillResultErrorValue);
        }
        else if (skillId > 0 && Connection.ActiveChar.Skills.IsVariantOfSkill(skillId))
        {
            // Variant of learned skill?
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject, false, out skillResultErrorValue);
        }
        else
        {
            // No idea what this is
            Logger.Warn($"StartSkill: Id {skillId}, undefined use type");
            // If it's a valid skill cast it. This fixes interactions with quest items/doodads.
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject, false, out skillResultErrorValue);
        }

        // HACKFIX: dismount from slave
        if (skillId == (uint)SkillConstants.Dismount)
        {
            var slave = (Slave)WorldManager.Instance.GetBaseUnit(skillCastTarget.ObjId);
            if (slave != null)
            {
                SlaveManager.Instance.UnbindSlave(Connection.ActiveChar, slave.TlId, AttachUnitReason.SlaveUnbinding);
            }
        }

        if (skillResult != SkillResult.Success)
        {
            // It actually sends a skill started packet, but not a skill fired or stopped
            var scSkillStartedPacket = new SCSkillStartedPacket(skillId, 0, skillCaster, skillCastTarget, skill, skillObject);
            scSkillStartedPacket.RealCastTimeDiv10 = 0;
            scSkillStartedPacket.BaseCastTimeDiv10 = 0;
            // ExtraData at the end of the packet is used to mark a use error
            scSkillStartedPacket.SetSkillResult(skillResult);
            scSkillStartedPacket.SetResultUInt(skillResultErrorValue);
            Connection.ActiveChar.SendPacket(scSkillStartedPacket);
        }
    }
}
