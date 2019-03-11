using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
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
            Skill skill, SkillObject skillObject, DateTime time)
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
                    case WorldInteractionGroup.Building: // TODO not done, debug only
                        if (target is House house)
                        {
                            // TODO remove resources...

                            var nextStep = house.CurrentStep + 1;
                            if (house.Template.BuildSteps.Count > nextStep)
                                house.CurrentStep = nextStep;
                            else
                                house.CurrentStep = -1;

                            // TODO to currStep +1 num action
                            character.BroadcastPacket(
                                new SCHouseBuildProgressPacket(
                                    house.TlId,
                                    house.ModelId,
                                    house.Template.BuildSteps.Count,
                                    nextStep
                                ),
                                true
                            );

                            if (house.CurrentStep == -1)
                            {
                                var doodads = house.AttachedDoodads.ToArray();
                                foreach (var doodad in doodads)
                                    doodad.Spawn();
                            }
                        }

                        break;
                    default:
                        _log.Warn("CraftEffect, {0} not have wi group", WorldInteraction);
                        break;
                }

                character.Quests.OnInteraction(WorldInteraction);
            }
        }
    }
}
