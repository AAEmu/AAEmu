using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncBuff : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint BuffId { get; set; }
        public float Radius { get; set; }
        public int Count { get; set; }
        public uint PermId { get; set; }
        public uint RelationshipId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncBuff");

        }
    }
}
