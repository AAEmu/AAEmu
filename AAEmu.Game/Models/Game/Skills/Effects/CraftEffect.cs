using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class CraftEffect : EffectTemplate
    {
        public WorldInteractionType WorldInteraction { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("CraftEffect, {0}", WorldInteraction);

            var wiGroup = WorldManager.Instance.GetWorldInteractionGroup((uint)WorldInteraction);
            if (caster is Character character)
            {
                switch (wiGroup)
                {
                    case WorldInteractionGroup.Craft:
                        if (target is Shipyard.Shipyard shipyard)
                        {
                            _log.Debug("[Shipyard] ID {0}, objID {1}", shipyard.ShipyardData.TemplateId, shipyard.ObjId);

                            shipyard.AddBuildAction();
                            //_log.Debug("[Shipyard] BaseAction {0}, NumAction {1}, CurrentAction {2}", shipyard.BaseAction, shipyard.NumAction, shipyard.CurrentAction);
                            //_log.Debug("[Shipyard] AllAction {0}, CurrentStep {1}, ShipyardSteps.Count {2}", shipyard.AllAction, shipyard.CurrentStep, shipyard.Template.ShipyardSteps.Count);
                            if (shipyard.CurrentStep == -1)
                            {
                                shipyard.ShipyardData.Actions = shipyard.AllAction;
                                shipyard.ShipyardData.Step = shipyard.Template.ShipyardSteps.Count;
                                _log.Debug("[Shipyard] Actions {0}, Step {1}", shipyard.AllAction, shipyard.Template.ShipyardSteps.Count);
                            }
                            else
                            {
                                shipyard.ShipyardData.Actions = shipyard.CurrentAction;
                                shipyard.ShipyardData.Step = shipyard.CurrentStep;
                                _log.Debug("[Shipyard] Actions {0}, Step {1}", shipyard.CurrentAction, shipyard.CurrentStep);
                            }
                            character.BroadcastPacket(new SCShipyardStatePacket(shipyard.ShipyardData), true);
                        }
                        character.Craft.EndCraft();
                        break;
                    case WorldInteractionGroup.Collect:
                        break;
                    case WorldInteractionGroup.Building when target is House house: // TODO not done, debug only
                        if (house.Template.BuildSteps.Count == 0)
                            house.CurrentStep = -1;
                        else
                            house.AddBuildAction();

                        character.BroadcastPacket(
                            new SCHouseBuildProgressPacket(
                                house.TlId,
                                house.ModelId,
                                house.AllAction,
                                house.CurrentStep == -1 ? house.AllAction : house.CurrentAction
                            ),
                            true
                        );

                        if (house.CurrentStep == -1)
                        {
                            var doodads = house.AttachedDoodads.ToArray();
                            foreach (var doodad in doodads)
                                doodad.Spawn();
                        }

                        break;
                    default:
                        _log.Warn("CraftEffect, {0} not have wi group", WorldInteraction);
                        if (target is Shipyard.Shipyard sy)
                        {
                            if (sy.ShipyardData.OwnerName == caster.Name)
                            {
                                ShipyardManager.Instance.ShipyardCompletedTask(sy);
                            }
                            else
                                caster.SendErrorMessage(ErrorMessageType.NoPermissionToLoot);
                        }
                        break;
                }

                character.Quests.OnInteraction(WorldInteraction, target);
            }
        }
    }
}
