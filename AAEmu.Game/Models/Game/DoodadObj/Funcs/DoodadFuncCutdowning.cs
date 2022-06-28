using AAEmu.Game.Models.Game.Char;

using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncCutdowning : DoodadFuncTemplate
    {
        // doodad_funcs
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if (caster is Character)
                _log.Debug("DoodadFuncCutdowning");
            else
                _log.Trace("DoodadFuncCutdowning");

            //TODO Tree falling effect goes here?
            // DoodadManager.Instance.TriggerFunc(GetType().Name, caster, owner, skillId);
            //owner.Use(caster, skillId);
            
            owner.ToNextPhase = true;
        }
    }
}
