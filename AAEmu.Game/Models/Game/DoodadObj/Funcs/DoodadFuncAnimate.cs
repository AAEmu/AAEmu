using System.Collections;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncAnimate : DoodadFuncTemplate
    {
        public string Name { get; set; }
        public bool PlayOnce { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if (!PlayOnce)
            { 
                //The client might handle this flag already
            }

        }
    }
}
