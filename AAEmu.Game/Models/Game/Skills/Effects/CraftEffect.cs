using System;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class CraftEffect : EffectTemplate
{
    public WorldInteractionType WorldInteraction { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Debug("CraftEffect, {0}", WorldInteraction);

        var wiGroup = WorldManager.Instance.GetWorldInteractionGroup((uint)WorldInteraction);

        // Check what skill triggered this build
        uint usedSkill = 0;
        if (castObj is CastSkill castSkill)
            usedSkill = castSkill.SkillId;

        // Check for empty world interaction type
        /*
        if (wiGroup == null)
        {
            Logger.Warn($"CraftEffect, wi {WorldInteraction} does not not have wi group using skill {usedSkill}, defaulting to Collect");
            wiGroup = WorldInteractionGroup.Collect;
        }
        */

        if (caster is Character character)
        {
            // Logger.Warn($"{character.Name} triggered wiGroup {wiGroup}({(int)wiGroup}) with wi {WorldInteraction}({(int)WorldInteraction})");
            switch (wiGroup)
            {
                case WorldInteractionGroup.Craft:
                    if (target is Shipyard.Shipyard shipyard)
                    {
                        Logger.Trace("[Shipyard] ID {0}, objID {1}", shipyard.ShipyardData.TemplateId, shipyard.ObjId);

                        var shipStep =
                            (shipyard.CurrentAction >= 0) && (shipyard.CurrentStep < shipyard.Template.ShipyardSteps.Count)
                                ? shipyard.Template.ShipyardSteps[shipyard.CurrentStep]
                                : null;

                        // Compare if the used skill (correct pack) is still valid when used
                        if ((shipStep != null) && (usedSkill != shipStep.SkillId))
                        {
                            Logger.Warn("{0} tried to build a ship using the wrong skill, {1} instead of {2}", caster.Name, usedSkill, shipStep.SkillId);
                            source.Skill.Cancelled = true;
                        }
                        else
                        {
                            shipyard.AddBuildAction();
                            //Logger.Trace("[Shipyard] BaseAction {0}, NumAction {1}, CurrentAction {2}", shipyard.BaseAction, shipyard.NumAction, shipyard.CurrentAction);
                            //Logger.Trace("[Shipyard] AllAction {0}, CurrentStep {1}, ShipyardSteps.Count {2}", shipyard.AllAction, shipyard.CurrentStep, shipyard.Template.ShipyardSteps.Count);
                            if (shipyard.CurrentStep == -1)
                            {
                                shipyard.ShipyardData.Actions = shipyard.AllAction;
                                shipyard.ShipyardData.Step = shipyard.Template.ShipyardSteps.Count;
                                Logger.Trace("[Shipyard] Actions {0}, Step {1}", shipyard.AllAction, shipyard.Template.ShipyardSteps.Count);
                            }
                            else
                            {
                                shipyard.ShipyardData.Actions = shipyard.CurrentAction;
                                shipyard.ShipyardData.Step = shipyard.CurrentStep;
                                Logger.Trace("[Shipyard] Actions {0}, Step {1}", shipyard.CurrentAction, shipyard.CurrentStep);
                            }

                            character.BroadcastPacket(new SCShipyardStatePacket(shipyard.ShipyardData), true);
                            character.Craft.EndCraft();
                        }
                    }
                    else
                    {
                        character.Craft.EndCraft();
                    }
                    break;
                case WorldInteractionGroup.Collect:
                    character.Craft.EndCraft();
                    break;
                case WorldInteractionGroup.Building when target is House house:
                    // Get the house's current build step
                    var currentStep =
                        (house.CurrentAction >= 0) && (house.CurrentStep < house.Template.BuildSteps.Count)
                            ? house.Template.BuildSteps[house.CurrentStep]
                            : null;

                    // Compare if the used skill (correct pack) is still valid when used
                    if ((currentStep != null) && (usedSkill != currentStep.SkillId))
                    {
                        Logger.Warn("{0} tried to building using the wrong skill, {1} instead of {2}", caster.Name, usedSkill, currentStep.SkillId);
                        character.SkillTask.Skill.Cancelled = true;
                        character.InterruptSkills();
                    }
                    else
                    {
                        // When done, set step to -1
                        if (house.Template.BuildSteps.Count == 0)
                            house.CurrentStep = -1;
                        else
                            house.AddBuildAction();

                        // Send build packet
                        character.BroadcastPacket(
                            new SCHouseBuildProgressPacket(
                                house.TlId,
                                house.ModelId,
                                house.AllAction,
                                house.CurrentStep == -1 ? house.AllAction : house.CurrentAction
                            ),
                            true
                        );

                        // When done, spawn all attached doodads like doors and windows
                        if (house.CurrentStep == -1)
                        {
                            var doodads = house.AttachedDoodads.ToArray();
                            foreach (var doodad in doodads)
                                doodad.Spawn();
                        }
                    }
                    break;
                default:
                    Logger.Warn($"CraftEffect, {WorldInteraction} does not have a wi group ({wiGroup})");
                    if (target is Shipyard.Shipyard sy)
                    {
                        if (sy.ShipyardData.OwnerName == caster.Name)
                        {
                            ShipyardManager.ShipyardCompletedTask(sy);
                        }
                        else
                            character.SendErrorMessage(ErrorMessageType.NoPermissionToLoot);
                    }
                    else
                    {
                        character.Craft.EndCraft();
                    }
                    break;
            }

            //character.Quests.OnInteraction(WorldInteraction, target);
            // инициируем событие
            Task.Run(() => QuestManager.Instance.DoInteractionEvents((Character)caster, target.TemplateId));
        }
    }
}
