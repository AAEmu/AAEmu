using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class GroundBuild : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster,
            uint skillId)
        {
            // TODO check permission caster
            if (target is House house)
            {
                // TODO remove resources...

                var nextStep = house.CurrentStep + 1;
                if (house.Template.BuildSteps.Count > nextStep)
                    house.CurrentStep = nextStep;
                else
                    house.CurrentStep = -1;

                // TODO to currStep +1 num action
                ((Character)caster).BroadcastPacket(
                    new SCHouseBuildProgressPacket(
                        house.TlId,
                        house.ModelId,
                        house.Template.BuildSteps.Count,
                        house.CurrentStep
                    ),
                    true
                );
            }
        }
    }
}
