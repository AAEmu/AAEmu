using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.Debug;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartSkillPacket() : GamePacket(CSOffsets.CSStartSkillPacket, 1)
{
    /// <summary>
    /// The artifact window appends a tail after <c>inputDirection</c> that no other part of the packet carries,
    /// and the byte read as <c>inputDirection</c> is itself the equipment slot the window is working on (head 0,
    /// chest 2, legs 4 — measured against the window's own slot). Two casts are known:
    /// <list type="bullet">
    /// <item>the feed, whose tail is the <c>equip_slot_reinforce_materials</c> row the player picked
    /// (<c>u16</c>, then four bytes that stay zero), 24-byte packet;</item>
    /// <item>the Replace button, whose tail is the tier it is replacing — the trigger level whose effect line
    /// the player selected (<c>u16</c>), 20-byte packet, and the skill is
    /// <see cref="CharacterEquipSlotReinforces.ReplaceEffectSkillId"/>.</item>
    /// </list>
    /// Both are queued rather than spent: the cast still has its casting time to run, and the special effect is
    /// what acts on the choice when it lands.
    /// </summary>
    private const int ReinforceFeedTailBytes = sizeof(ushort) + sizeof(int);

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
        // desynced the stream. Type 15 is ActiveAbilitySet (skillsaver slot as i32) on 10.0.2.13.
        // Type lives in the low 6 bits (same as SC SkillCastExtra); mask 0x0F truncated higher types.
        var flag = stream.ReadByte();
        var flagType = flag & 0x3F;
        SkillObject skillObject;

        // In 10.0.2.13 this byte is a bit MASK, not a SkillObjectType. The original server's parser
        // splits out bits 0-3 individually, and only bit 3 pulls a payload: thirteen ints, right
        // before inputDirection. Reading it as `flag & 15` therefore invented types that do not
        // exist - a dye cast sends flag 27 (0b11011), which looked like type 11, was clamped, and
        // its thirteen ints (the chosen colour among them) were discarded.
        //
        // Deliberately additive: flags that already resolve to a known type keep the old path
        // untouched, because portals and the other Unk* shapes are wired to it. Only bit 3, and
        // only where the old code gave up anyway, is newly honoured.
        //
        // A type this server models is parsed as itself, and that has to be asked first: synthesis
        // sends flag 8, which is both "bit 3 set" and a real type - the material slots - so the
        // generic thirteen-int read would otherwise swallow it and lose which infusions were placed.
        // AbilitySet (15) is registered in IsKnownType so skillsaver activate keeps its slot payload.
        var hasExtraValues = (flag & 8) != 0;
        if (HousingGameData.Instance.IsRebuildSkill(skillId)
            && flagType == (int)SkillObjectType.ItemGradeEnchantingSupport)
        {
            // Flag 7 is bits 0-2 on remodel Confirm, and the first u32 is the housing template.
            // Reading it as grade-enchant consumes the rest of the extra and SkillStarted then
            // echoes type 7, which the client cannot parse (sc error on SkillStarted).
            skillObject = HousingRebuildSkillCast.ForSkillStarted(stream.ReadUInt32());
        }
        else if (SkillObject.IsKnownType(flagType))
        {
            skillObject = SkillObject.GetByType((SkillObjectType)flagType);
            skillObject.Read(stream);
        }
        else if (hasExtraValues)
        {
            var extraValues = new SkillObjectExtraValues();
            extraValues.Read(stream);
            skillObject = extraValues;

            // Logged for every skill that carries the block, not just dyeing, so the change can be
            // judged on more than one case - the nest interaction sends flag 28 and lands here too.
            // Drop to Debug once the meaning of the values is settled.
            Logger.Info(
                "StartSkill extras: skillId={0} flag={1} count={2} values=[{3}]",
                skillId,
                flag,
                extraValues.ReadCount,
                string.Join(" ", extraValues.Values.Take(extraValues.ReadCount).Select(v => v.ToString("X8"))));

            // The board's fill cast names the order in this block (low then high). Queue it so a
            // cancelled cast that still fires the effect, or a missing skill-object, can still
            // resolve the row.
            if (CraftOrderContent.IsCraftOrderSkill(skillId) &&
                Connection.ActiveChar != null &&
                CraftOrderProcessRules.TryReadOrderId(extraValues, out var processOrderId))
                CraftOrderManager.Instance.QueueProcessOrder(Connection.ActiveChar.Id, processOrderId);
        }
        else
        {
            if (flagType != 0)
                Logger.Warn($"StartSkill: skillObject flag={flag} type={flagType} clamped to None");
            skillObject = new SkillObject();
        }

        if (CraftOrderContent.IsRestoreSheetSkill(skillId) && Connection.ActiveChar != null)
        {
            var sheetId = skillCaster is SkillItem itemCaster ? itemCaster.ItemId : 0ul;
            if (sheetId == 0 && skillObject is SkillObjectExtraValues restoreExtras)
                CraftOrderProcessRules.TryReadOrderId(restoreExtras, out sheetId);
            if (sheetId != 0)
                CraftOrderManager.Instance.QueueRestoreSheet(Connection.ActiveChar.Id, sheetId);
        }
        // The byte below is normally the input direction, and the artifact window overloads it with the
        // equipment slot its two casts work on. The craft order sheet skill replaces it entirely: the
        // craft the player picked in the folio, how many, then one trailing byte. Measured on live casts
        // — the same craft with count 1 and count 2 differed in exactly those four bytes.
        byte inputDirection = 0;
        if (CraftOrderContent.IsMakeSheetSkill(skillId) &&
            Connection.ActiveChar != null &&
            stream.LeftBytes >= CraftOrderSheetRules.CastTailBytes)
        {
            var craftId = stream.ReadUInt32();
            var sheetCount = stream.ReadUInt32();
            _ = stream.ReadByte();
            CraftOrderManager.Instance.QueueSheetCraft(Connection.ActiveChar.Id, craftId, sheetCount);
        }
        else
        {
            inputDirection = stream.ReadByte();
        }

        // The artifact window's Replace button: the tail is the tier whose effect line the player selected.
        if (skillId == CharacterEquipSlotReinforces.ReplaceEffectSkillId && stream.LeftBytes >= sizeof(ushort))
        {
            var triggerLevel = stream.ReadUInt16();
            Connection?.ActiveChar?.EquipSlotReinforces.QueueEffectReplace(inputDirection, triggerLevel);
        }
        // The artifact window's Confirm button: the tail names the material row the player picked for the slot
        // in the byte above. Read it only where it can mean something - a ladder slot - so another skill's tail
        // is never mistaken for one.
        else if (skillId == CharacterEquipSlotReinforces.FeedSkillId && stream.LeftBytes >= ReinforceFeedTailBytes)
        {
            var materialRowId = stream.ReadUInt16();
            _ = stream.ReadInt32();
            Connection?.ActiveChar?.EquipSlotReinforces.QueueWindowFeed(inputDirection, materialRowId);
        }

        HarpoonMechanicsDebug.LogCsStartSkillIfHarpoon(skillId, flag, flagType, skillCaster, skillCastTarget, skillObject);

        var activeCharacter = Connection.ActiveChar;
        activeCharacter.LastPacketActivityTime = DateTime.UtcNow;

        // Heir successors are not ordinary learned skills. Gate them before the Zone-authority/local
        // split so neither path nor the generic valid-template fallback can cast an unselected variant.
        if (HeirGameData.Instance.TryGetHeirSkillForSuccessor(skillId, out _, out _) &&
            (!activeCharacter.HeirSkills.IsActiveSuccessor(skillId) ||
             skillCaster is not SkillCasterUnit heirCaster || heirCaster.ObjId != activeCharacter.ObjId))
        {
            // No published result code proves what a forged request should get back. Fail closed
            // rather than fabricating an SCSkillStarted result the server may not send.
            Logger.Warn("StartSkill rejected unselected Heir successor {0} for {1}", skillId, activeCharacter.Name);
            return;
        }

        // Racial defaults live in character_default_skills. Gate them before the
        // Zone-authority return so HandleZoneAuthorityCast cannot cast another race's kit.
        if (!DefaultSkillAssignRules.AllowDefaultCast(
                SkillManager.Instance.IsDefaultSkill(skillId),
                SkillManager.Instance.IsDefaultSkill(skillId, activeCharacter.Race, activeCharacter.Gender)))
        {
            Logger.Warn("StartSkill rejected other-race default {0} for {1}", skillId, activeCharacter.Name);
            return;
        }

        var world = Connection.ActiveChar?.ParentWorld ?? WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);

        Logger.Info($"StartSkill: Id {skillId}, flag {flag}, caster={skillCaster.ObjId}, target={skillCastTarget.ObjId}");

        // Skillsaver apply: stash slot before zone/local split so ActivateSavedAbilitySet can finish it.
        if (skillId == CharacterAbilitySets.ActivateSkillId)
            StashAbilitySetActivationSlot(activeCharacter, skillObject);
        if (skillId == BlessUthstinRules.SelectSkillId)
            StashBlessUthstinSelectPage(activeCharacter, skillObject);

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
        else if (SkillManager.Instance.IsDefaultSkill(skillId, Connection.ActiveChar.Race, Connection.ActiveChar.Gender) || SkillManager.Instance.IsCommonSkill(skillId) && skillCaster is not SkillItem)
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
        else if (Connection.ActiveChar.Skills.HasSkill(skillId))
        {
            // Is it one of our learned character skills, or one a live buff grants?
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
            // The fallback used to cast any valid template, so a forged skill id the character never
            // learned was accepted and its effects ran. Ability skills now have to be held: learned, or
            // granted by a live buff (CharacterSkills.HasSkill covers both). Basic attacks, racial
            // defaults, common skills and item casts keep the permissive path — quest items and doodad
            // interactions arrive here.
            var fallbackTemplate = SkillManager.Instance.GetSkillTemplate(skillId);
            skill = new Skill(fallbackTemplate);
            if (!SkillUseConditionRules.AllowsUnlearnedCast(
                    fallbackTemplate?.AbilityId ?? 0,
                    isItemCast: false,
                    isDefaultSkill: SkillManager.Instance.IsDefaultSkill(skillId),
                    isCommonSkill: SkillManager.Instance.IsCommonSkill(skillId)) &&
                !Connection.ActiveChar.Skills.HasSkill(skillId))
            {
                Logger.Warn("StartSkill rejected unlearned ability skill {0} for {1}", skillId, Connection.ActiveChar.Name);
                SendSkillResult(skillId, skillCaster, skillCastTarget, skillObject, skill,
                    SkillUseConditionRules.UnlearnedSkillResult);
                return;
            }

            // If it's a valid skill cast it. This fixes interactions with quest items/doodads.
            skillResult = skill.Use(Connection.ActiveChar, skillCaster, skillCastTarget, skillObject, false,
                out skillResultErrorValueUShort, out skillResultErrorValue);
        }

        if (skillResult != SkillResult.Success)
        {
            SendSkillResult(skillId, skillCaster, skillCastTarget, skillObject, skill, skillResult,
                skillResultErrorValueUShort, skillResultErrorValue);
        }
    }

    /// <summary>
    /// The result packet for a rejected cast: a SkillStarted with no cast time whose extra data carries
    /// the error, which is what the local path has always sent and what the client shows.
    /// </summary>
    private void SendSkillResult(uint skillId, SkillCaster skillCaster, SkillCastTarget skillCastTarget,
        SkillObject skillObject, Skill skill, SkillResult result, ushort resultUShort = 0, uint resultUInt = 0)
    {
        var packet = new SCSkillStartedPacket(skillId, 0, skillCaster, skillCastTarget, skill, skillObject)
        {
            RealCastTimeDiv10 = 0, BaseCastTimeDiv10 = 0
        };
        packet.SetSkillResult(result);
        packet.SetResultUShort(resultUShort);
        packet.SetResultUInt(resultUInt);
        Connection.ActiveChar.SendPacket(packet);
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

        // A mount/hull cast is made on the rider's behalf but the skill still has to be one the
        // character holds; the check is the same on both dispatch paths (this one never consulted
        // Character.Skills at all).
        if (skillCaster is not SkillItem &&
            !SkillUseConditionRules.AllowsUnlearnedCast(
                template.AbilityId,
                isItemCast: false,
                isDefaultSkill: SkillManager.Instance.IsDefaultSkill(skillId),
                isCommonSkill: SkillManager.Instance.IsCommonSkill(skillId)) &&
            !character.Skills.HasSkill(skillId))
        {
            Logger.Warn("ZoneAuthority StartSkill rejected unlearned ability skill {0} for {1}", skillId, character.Name);
            SendSkillResult(skillId, skillCaster, skillCastTarget, skillObject, new Skill(template),
                SkillUseConditionRules.UnlearnedSkillResult);
            return;
        }

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

        // Authored interaction plots that start as original-source self-casts run on the NPC the
        // player picked. The client still names the player as caster; leaving it that way applies
        // the graph to the player and the NPC never receives the follow-up skill.
        if (casterUnit == character)
        {
            var interactionTarget = world.GetUnit(skillCastTarget.ObjId);
            var authored = interactionTarget is Npc { Template: not null } offered
                ? NpcInteractionGameData.Instance.GetSkills(offered.Template.NpcInteractionSetId)
                : [];
            if (NpcInteractionCastRules.TryResolveNpcCaster(
                    character, interactionTarget, skillId, template, authored, out var interactionNpc))
            {
                Logger.Info(
                    "StartSkill interaction remap skill={0} player={1} npc={2} tpl={3}",
                    skillId, character.Name, interactionNpc.ObjId, interactionNpc.TemplateId);
                casterUnit = interactionNpc;
                skillCaster = new SkillCasterUnit(interactionNpc.ObjId);
            }
        }

        var skill = new Skill(template);

        var skillResult = skill.Use(casterUnit, skillCaster, skillCastTarget, skillObject, false,
            out var skillResultErrorValueUShort, out var skillResultErrorValue);
        if (skillResult != SkillResult.Success)
        {
            // Don't poison the melee hotbar with CooldownTime fails — client auto-retries skill 2/3/4.
            if (skillResult == SkillResult.CooldownTime &&
                (skillId is 2 or 3 or 4 ||
                 template.StartAutoAttack ||
                 SkillCastOverlapRules.IsInstantComboHit(template.CastingTime, template.CustomGcd)))
            {
                Logger.Trace("ZoneAuthority hold/combo CooldownTime skillId={0} (suppressed fail packet)", skillId);
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
            else if (skillResult == SkillResult.CooldownTime &&
                     SportFishCombat.IsFishingHoldSkill(
                         template.TargetType, SkillManager.Instance.GetSkillTags(skillId)))
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

    /// <summary>
    /// Skillsaver apply casts skill 32189; the special effect needs the slot index the UI picked.
    /// Prefer skill-object payloads when present (Unk5.Step / Unk1.Id).
    /// </summary>
    private static void StashAbilitySetActivationSlot(Character character, SkillObject skillObject)
    {
        // ActiveAbilitySet is i16 on the wire (see SkillObjectAbilitySet). Legacy Unk5/Unk1 kept as fallback.
        var slot = skillObject switch
        {
            SkillObjectAbilitySet abilitySet => abilitySet.SlotIndex,
            SkillObjectUnk5 unk5 => unk5.Step,
            SkillObjectUnk1 unk1 => unk1.Id,
            _ => -1
        };

        if (slot < 0)
        {
            Logger.Warn(
                "AbilitySet activate stash {0}: no slot in skillObject type={1}",
                character.Name, skillObject?.Flag);
            return;
        }

        Logger.Info("AbilitySet activate stash {0}: slot {1} (skillObject={2})", character.Name, slot, skillObject.Flag);
        character.AbilitySets?.SetPendingActivationSlot(slot);
    }

    /// <summary>
    /// Bless Uthstin activate casts skill 37244; the special effect needs the 0-based page.
    /// </summary>
    private static void StashBlessUthstinSelectPage(Character character, SkillObject skillObject)
    {
        var page = skillObject switch
        {
            SkillObjectBlessUthstinPage uthstin => uthstin.PageIndex,
            SkillObjectUnk5 unk5 => unk5.Step,
            _ => -1
        };

        if (page < 0)
        {
            Logger.Warn(
                "BlessUthstin select stash {0}: no page in skillObject type={1}",
                character.Name, skillObject?.Flag);
            return;
        }

        character.BlessUthstin?.SetPendingSelectPage(page);
    }
}
