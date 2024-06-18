using System;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartSkillPacket : GamePacket
{
    public CSStartSkillPacket() : base(CSOffsets.CSStartSkillPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Will delay for 150 Milliseconds to eliminate the hanging of the skill
        using var source = new CancellationTokenSource();
        var t = Task.Run(async delegate
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), source.Token);
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

        var skillCasterType = stream.ReadByte();
        var skillCaster = SkillCaster.GetByType((SkillCasterType)skillCasterType);
        skillCaster.Read(stream);

        var skillCastTargetType = stream.ReadByte();
        var skillCastTarget = SkillCastTarget.GetByType((SkillCastTargetType)skillCastTargetType);
        skillCastTarget.Read(stream);

        var flag = stream.ReadByte();
        var flagType = flag & 15;
        var skillObject = SkillObject.GetByType((SkillObjectType)flagType);
        if (flagType > 0) skillObject.Read(stream);

        Logger.Info($"StartSkill: Id {skillId}, flag {flag}, caster={skillCaster.ObjId}, target={skillCastTarget.ObjId}");
        
        var skillResult = SkillResult.Success;
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
            if (skill.Use(caster, skillCaster, skillCastTarget, skillObject) != SkillResult.Success)
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
        else if (SkillManager.Instance.IsDefaultSkill(skillId) || SkillManager.Instance.IsCommonSkill(skillId) && !(skillCaster is SkillItem))
        {
            // Is it a common skill?
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO: переделать / rewrite ...
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
        }
        else if (skillCaster is SkillItem si)
        {
            // A skill triggered by a item
            var player = Connection.ActiveChar; 
            var item = player.Inventory.GetItemById(si.ItemId);
            // добавил проверку на ItemBindType.BindOnPickup для записи портала с помощью камина в доме
            if (item == null || skillId != item.Template.UseSkillId && item.Template.BindType != ItemBindType.BindOnPickup)
                return;
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skillResult = skill.Use(player, skillCaster, skillCastTarget, skillObject);
        }
        else if (Connection.ActiveChar.Skills.Skills.ContainsKey(skillId))
        {
            // Is it one of our learned character skills?
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            skill = new Skill(template, Connection.ActiveChar);
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
        }
        else if (skillId > 0 && Connection.ActiveChar.Skills.IsVariantOfSkill(skillId))
        {
            // Variant of learned skill?
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
        }
        else
        {
            // No idea what this is
            Logger.Warn($"StartSkill: Id {skillId}, undefined use type");
            // If it's a valid skill cast it. This fixes interactions with quest items/doodads.
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
        }
        
        if (skillResult != SkillResult.Success)
        {
            // It actually sends a skill started packet, but not a skill fired or stopped
            var scSkillStartedPacket = new SCSkillStartedPacket(skillId, 0, skillCaster, skillCastTarget, skill, skillObject);
            scSkillStartedPacket.RealCastTimeDiv10 = 0;
            scSkillStartedPacket.BaseCastTimeDiv10 = 0;
            // ExtraData at the end of the packet is used to mark a use error
            scSkillStartedPacket.ExtraData = 1;
            scSkillStartedPacket.ExtraDataByte = 1; // "Can't use this" (example captures of 5.0.7.0 show 0x66 here for skill 20444 (Drop the Vase Near the River)
            // 1 - Can't use
            // 2 - Can't be used while dead
            // 3 - Can only be used while dead
            // 4 - Can't be used on a dead target
            // 5 - Target is already destroyed
            // 6 - Can't be used on a living target
            // 7 - Already performing a action
            // 8 - Can't be used right now
            // 9 - Select a target
            // 10 - Insufficient health to use this
            // 11 - Insufficient mana to use this
            // 12 - No line of sight
            // 13 - Target is on a different elevation
            
            
            Connection.ActiveChar.BroadcastPacket(scSkillStartedPacket, true);
        }
    }
}
