using System;
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

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("CraftEffect, {0}", WorldInteraction);

            var wiGroup = WorldManager.Instance.GetWorldInteractionGroup((uint)WorldInteraction);
            if (caster is Character character)
            {
                switch (wiGroup)
                {
                    case WorldInteractionGroup.Craft:
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
                        break;
                }

                character.Quests.OnInteraction(WorldInteraction,target);
            }
        }
    }
}
