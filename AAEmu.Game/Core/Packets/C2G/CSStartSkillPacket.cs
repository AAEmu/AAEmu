using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.Debug;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartSkillPacket() : GamePacket(CSOffsets.CSStartSkillPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Ignore if there is no active character set
        if (Connection.ActiveChar == null)
            return;

        var skillId = stream.ReadUInt32();

        var skillCasterType = stream.ReadByte();
        var skillCaster = SkillCaster.GetByType((SkillCasterType)skillCasterType);
        skillCaster.Read(stream);

        var skillCastTargetType = stream.ReadByte();
        var skillCastTarget = SkillCastTarget.GetByType((SkillCastTargetType)skillCastTargetType);
        skillCastTarget.Read(stream);

        // Nest interact sent flag=28 → type=12 (invalid). Old path set SkillObject.Flag=12 and
        var flag = stream.ReadByte();
        var flagType = flag & 15;
        SkillObject skillObject;
        if (flagType == 0 || flagType > (int)SkillObjectType.ItemGradeEnchantingSupport)
        {
            if (flagType != 0)
                Logger.Warn($"StartSkill: skillObject flag={flag} type={flagType} clamped to None");
            skillObject = new SkillObject();
        }
        else
        {
            skillObject = SkillObject.GetByType((SkillObjectType)flagType);
            skillObject.Read(stream);
        }
        // Always present on CS wire after SkillCastExtra payload.
        _ = stream.ReadByte(); // inputDirection

        HarpoonMechanicsDebug.LogCsStartSkillIfHarpoon(skillId, flag, flagType, skillCaster, skillCastTarget, skillObject);

        var activeCharacter = Connection.ActiveChar;
        activeCharacter.LastPacketActivityTime = DateTime.UtcNow;

        // Heir successors are not ordinary learned skills. Gate them before the Zone-authority/local
        // split so neither path nor the generic valid-template fallback can cast an unselected variant.
        if (HeirGameData.Instance.TryGetHeirSkillForSuccessor(skillId, out _, out _) &&
            (!activeCharacter.HeirSkills.IsActiveSuccessor(skillId) ||
             skillCaster is not SkillCasterUnit heirCaster || heirCaster.ObjId != activeCharacter.ObjId))
        {
            // No commercial World binary is available to prove a result code for forged requests.
            // Fail closed without fabricating an SCSkillStarted result the native server may not use.
            Logger.Warn("StartSkill rejected unselected Heir successor {0} for {1}", skillId, activeCharacter.Name);
            return;
        }

        var world = Connection.ActiveChar?.ParentWorld ?? WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);

        Logger.Info($"StartSkill: Id {skillId}, flag {flag}, caster={skillCaster.ObjId}, target={skillCastTarget.ObjId}");

        // ZoneAuthority: Zone owns cast/effects. Forward WZSkillStarted + emit SC cast UX only.
        // Local Skill.Use builds plot CompressedGamePackets (DD04) that desync the client (sc error / zip fail).
        if (WorldIntegration.ZoneAuthority &&
            Environment.GetEnvironmentVariable("AAEMU_FORCE_LOCAL_SKILLS") != "1")
        {
            HandleZoneAuthorityCast(skillId, skillCaster, skillCastTarget, skillObject);
            return;
        }

        var skillResult = SkillResult.Success;
        var skillResultErrorValueUShort = (ushort)0;
        var skillResultErrorValue = 0u;
        Skill skill = null;

        if (skillCaster is SkillCasterUnit scu)
        {
            var unit = world.GetUnit(scu.ObjId);
            if (unit is Character character)
                Logger.Info($"{character.Name}:{character.ObjId} is using skill={skillId}");
        }

        if (skillCaster is SkillCasterMount scm)
        {
            // Mount or Slave skill
            Logger.Trace($"SkillCasterMount - MountSkillTemplateId {scm.MountSkillTemplateId}");
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));

            var caster = world.GetBaseUnit(skillCaster.ObjId);
            var mate = caster as Mate;
            var slave = caster as Slave;
            var mountAttachedSkill = 0u;

            if (mate != null || slave != null)
            {
                // check if it's a mate or slave skill and return its rider/operator related skill
                mountAttachedSkill = MateGameData.Instance.GetMountAttachedSkills(skillId, Connection.ActiveChar?.AttachedPoint ?? AttachPointKind.None);
            }

            // Use the main skill on the mate/slave
            var mountPrimaryResult = skill.Use(caster, skillCaster, skillCastTarget, skillObject, false,
                out skillResultErrorValueUShort, out skillResultErrorValue);
            skillResult = mountPrimaryResult;
            if (mountPrimaryResult == SkillResult.Success && slave != null)
            {
                if (skillId == HarpoonMechanicsDebug.ShipLaunchHarpoonSkillId)
                    ShipHarpoonRopeController.OnLaunchSucceeded(slave, skillCastTarget, Connection.ActiveChar);
                else if (skillId == HarpoonMechanicsDebug.ShipCutHarpoonRopeSkillId)
                    ShipHarpoonRopeController.OnCutRope(slave, Connection.ActiveChar);
            }

            // Successful mount skills without a rider helper have already emitted their normal cast packets.
            // Failures continue to the common SCSkillStarted result path so their native details reach the client.
            if (mountPrimaryResult == SkillResult.Success && mountAttachedSkill == 0)
                return;

            if (mountPrimaryResult == SkillResult.Success)
            {
                // Rider helper must target the mount (sail Interaction Use), not the player.
                skillResult = Connection.ActiveChar.UseSkill(mountAttachedSkill,
                    (caster as Unit) ?? Connection.ActiveChar);
            }
        }
        else if (Connection.ActiveChar.IsAutoAttack && skillId == Connection.ActiveChar.AutoAttackTask?.Skill?.Template?.Id)
        {
            // Same as already executing auto-skill, just send the success result.
            skill = Connection.ActiveChar.AutoAttackTask.Skill;
            skillResult = SkillResult.Success;
        }
        else if (SkillManager.Instance.IsDefaultSkill(skillId) || SkillManager.Instance.IsCommonSkill(skillId) && skillCaster is not SkillItem)
        {
            // Is it a common skill?
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO: переделать / rewrite ...
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject, false,
                out skillResultErrorValueUShort, out skillResultErrorValue);
            if (skillResult == SkillResult.Success && skillId < 5000 && skillCaster.ObjId == Connection.ActiveChar.ObjId)
            {
                // All basic combat skills are below ID 5000, only 2 (melee),3 (offhand) and 4 (ranged) exist, next actual skill used is 5001
                Connection.ActiveChar.IsAutoAttack = true;
                Connection.ActiveChar.StartAutoSkill(skill);
            }
        }
        else if (skillCaster is SkillItem si)
        {
            // A skill triggered by an item
            var player = Connection.ActiveChar;
            // var item = player.Inventory.GetItemById(si.ItemId);
            // добавил проверку на ItemBindType.BindOnPickup для записи портала с помощью камина в доме
            if (si.SkillSourceItem == null || skillId != si.SkillSourceItem.Template.UseSkillId && si.SkillSourceItem.Template.BindType != ItemBindType.BindOnPickup)
                return;
            // si.ItemTemplateId = item.TemplateId;
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skillResult = skill.Use(player, skillCaster, skillCastTarget, skillObject, false,
                out skillResultErrorValueUShort, out skillResultErrorValue);
        }
        else if (Connection.ActiveChar.Skills.Skills.ContainsKey(skillId))
        {
            // Is it one of our learned character skills?
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            skill = new Skill(template, Connection.ActiveChar);
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject, false,
                out skillResultErrorValueUShort, out skillResultErrorValue);
        }
        else if (skillId > 0 && Connection.ActiveChar.Skills.IsActiveHeirSuccessor(skillId))
        {
            // The selected Heir successor of a learned base skill.
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject, false,
                out skillResultErrorValueUShort, out skillResultErrorValue);
        }
        else
        {
            // No idea what this is
            Logger.Warn($"StartSkill: Id {skillId}, undefined use type");
            // If it's a valid skill cast it. This fixes interactions with quest items/doodads.
            skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject, false,
                out skillResultErrorValueUShort, out skillResultErrorValue);
        }

        if (skillResult != SkillResult.Success)
        {
            // It actually sends a skill started packet, but not a skill fired or stopped
            var scSkillStartedPacket = new SCSkillStartedPacket(skillId, 0, skillCaster, skillCastTarget, skill, skillObject)
            {
                RealCastTimeDiv10 = 0, BaseCastTimeDiv10 = 0
            };
            // ExtraData at the end of the packet is used to mark a use error
            scSkillStartedPacket.SetSkillResult(skillResult);
            scSkillStartedPacket.SetResultUShort(skillResultErrorValueUShort);
            scSkillStartedPacket.SetResultUInt(skillResultErrorValue);
            Connection.ActiveChar.SendPacket(scSkillStartedPacket);
        }
    }

    private void HandleZoneAuthorityCast(uint skillId, SkillCaster skillCaster, SkillCastTarget skillCastTarget, SkillObject skillObject)
    {
        var character = Connection.ActiveChar;
        var world = character?.ParentWorld ?? WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        skillObject ??= new SkillObject();

        if (template == null)
        {
            Logger.Warn("ZoneAuthority StartSkill unknown template skillId={0}", skillId);
            return;
        }

        // Client spam of basic attack while auto-attack task owns pacing — ACK only (local path does this).
        if (skillId is 2 or 3 or 4 &&
            character.IsAutoAttack &&
            character.AutoAttackTask?.Skill?.Template?.Id == skillId)
        {
            return;
        }

        // DB stop_autoattack (Firebolt etc.): cancel weapon AA so the cast isn't mixed with melee swings.
        if (template.StopAutoAttack && skillId is not (2 or 3 or 4))
            character.StopAutoSkill(character);

        // Helm / mount bar: caster is the hull (or mate), not the rider. Figurehead skills gate on
        // unit_reqs kind Buff against the ship's item_grade_buffs; casting as the Character always
        // fails that check (logged as UrkEquipSlot via the urk_buff→75 mapping). Gale / Quick Turn
        // "succeed" as the player but their effects never land on the boat.
        BaseUnit casterUnit = character;
        uint mountAttachedSkill = 0;
        if (skillCaster is SkillCasterMount)
        {
            var mount = world.GetBaseUnit(skillCaster.ObjId);
            if (mount is Slave or Mate)
            {
                casterUnit = mount;
                mountAttachedSkill = MateGameData.Instance.GetMountAttachedSkills(
                    skillId, character.AttachedPoint);
            }
            else
            {
                Logger.Warn("ZoneAuthority mount skill {0}: no Slave/Mate for bc={1}", skillId, skillCaster.ObjId);
                // Unlock the slot without fabricating a use timeline.
                character.ResetSkillCooldown(skillId, true);
                return;
            }
        }
        else
        {
            // Pet hotbar sometimes arrives as SkillCasterUnit(player). Casting Scratch (17701) as
            // the owner fails TooFarRange at the player's feet and greys the icon until CD ends.
            var mateCaster = FindActiveMateForSkill(character, skillId);
            if (mateCaster != null)
            {
                casterUnit = mateCaster;
                var mountSkillId = MateGameData.Instance.GetMountSkillIdBySkillId(skillId);
                skillCaster = new SkillCasterMount(mateCaster.ObjId)
                {
                    MountSkillTemplateId = mountSkillId
                };
            }
        }

        var skill = new Skill(template);

        var skillResult = skill.Use(casterUnit, skillCaster, skillCastTarget, skillObject, false,
            out var skillResultErrorValueUShort, out var skillResultErrorValue);
        if (skillResult != SkillResult.Success)
        {
            // Don't poison the melee hotbar with CooldownTime fails — client auto-retries skill 2/3/4.
            if (skillId is 2 or 3 or 4 && skillResult == SkillResult.CooldownTime)
            {
                Logger.Trace("ZoneAuthority basic-attack CooldownTime skillId={0} (suppressed fail packet)", skillId);
                return;
            }

            var fail = new SCSkillStartedPacket(skillId, 0, skillCaster, skillCastTarget, skill, skillObject)
            {
                RealCastTimeDiv10 = 0,
                BaseCastTimeDiv10 = 0
            };
            fail.SetSkillResult(skillResult);
            fail.SetResultUShort(skillResultErrorValueUShort);
            fail.SetResultUInt(skillResultErrorValue);
            character.SendPacket(fail);
            // Clear the local cooldown after failures that should immediately unlock the slot.
            if (skillResult is SkillResult.TooFarRange or SkillResult.TooCloseRange or SkillResult.NoTarget
                or SkillResult.InvalidSource or SkillResult.Failure)
                character.ResetSkillCooldown(skillId, true);
            Logger.Warn("ZoneAuthority Use failed skillId={0} result={1} caster={2}", skillId, skillResult, casterUnit.ObjId);
            return;
        }

        if (casterUnit is Slave slave)
        {
            if (skillId == HarpoonMechanicsDebug.ShipLaunchHarpoonSkillId)
                ShipHarpoonRopeController.OnLaunchSucceeded(slave, skillCastTarget, character);
            else if (skillId == HarpoonMechanicsDebug.ShipCutHarpoonRopeSkillId)
                ShipHarpoonRopeController.OnCutRope(slave, character);
        }

        if (mountAttachedSkill != 0)
        {
            // Sail Interaction helpers (e.g. 28228) must run against the mount/hull, not the rider.
            var mountUnit = casterUnit as Unit;
            character.UseSkill(mountAttachedSkill, mountUnit ?? character);
        }

        // Basic attacks: start server-paced auto-attack (weapon delay). Fresh Skill instance for the
        // loop — the Use() instance already EndSkill'd (TlId=0).
        if (skillId is 2 or 3 or 4 && skillCaster.ObjId == character.ObjId)
        {
            character.StartAutoSkill(new Skill(template));
        }

        Logger.Info("ZoneAuthority Use+WZ skillId={0} tl={1} plotOnly={2} hasPlot={3} castMs={4} auto={5} caster={6}",
            skillId, skill.TlId, template.PlotOnly, template.Plot != null, template.CastingTime, character.IsAutoAttack,
            casterUnit.ObjId);
    }

    private static Mate FindActiveMateForSkill(Character character, uint skillId)
    {
        var mates = character.ParentWorld?.MateManager?.GetActiveMates(character.Id);
        if (mates == null)
            return null;

        foreach (var mate in mates)
        {
            if (mate != null && MateGameData.Instance.NpcHasMountSkill(mate.TemplateId, skillId))
                return mate;
        }

        return null;
    }
}
