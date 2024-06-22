using System;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
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
            var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));

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
            skill.Use(caster, skillCaster, skillCastTarget, skillObject);

            // If no rider/operator skill is linked, we can stop here
            if (mountAttachedSkill == 0)
                return;

            // Use player's currently selected for the rider/operator skill
            var riderTarget = Connection.ActiveChar.CurrentTarget as Unit;

            // Execute the rider/operator skill as the player using either target or self
            Connection.ActiveChar.UseSkill(mountAttachedSkill, riderTarget ?? Connection.ActiveChar);
        }
        else if (SkillManager.Instance.IsDefaultSkill(skillId) || SkillManager.Instance.IsCommonSkill(skillId) && !(skillCaster is SkillItem))
        {
            // Is it a common skill?

            // Bugfix: Prevent the player from picking up packs from the ground while already equipping one
            if (skillId == 11361 && !Connection.ActiveChar.Inventory.CanReplaceGliderInBackpackSlot())
            {
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.BackpackOccupied);
                return;
            }
            
            var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO: переделать / rewrite ...
            skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
        }
        else if (skillCaster is SkillItem si)
        {
            // A skill triggered by a item
            var item = Connection.ActiveChar.Inventory.GetItemById(((SkillItem)skillCaster).ItemId);
            // добавил проверку на ItemBindType.BindOnPickup для записи портала с помощью камина в доме
            if (item == null || skillId != item.Template.UseSkillId && item.Template.BindType != ItemBindType.BindOnPickup)
                return;
            var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
        }
        else if (Connection.ActiveChar.Skills.Skills.ContainsKey(skillId))
        {
            // Is it one of our learned character skills?
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            var skill = new Skill(template, Connection.ActiveChar);
            skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
        }
        else if (skillId > 0 && Connection.ActiveChar.Skills.IsVariantOfSkill(skillId))
        {
            // Variant of learned skill?
            var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
        }
        else
        {
            // No idea what this is
            Logger.Warn("StartSkill: Id {0}, undefined use type", skillId);
            //If its a valid skill cast it. This fixes interactions with quest items/doodads.
            var unskill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            unskill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
        }
    }
}
