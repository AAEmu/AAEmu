using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncCloutEffect : DoodadFuncTemplate
    {
        public uint FuncCloutId { get; set; }
        public uint EffectId { get; set; }

        // doodad_funcs
        public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncCloutEffect");
        }
    }
}
