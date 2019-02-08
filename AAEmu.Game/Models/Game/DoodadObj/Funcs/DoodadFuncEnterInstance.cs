using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncEnterInstance : DoodadFuncTemplate
    {
        public uint ZoneId { get; set; }
        public uint ItemId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncEnterInstance, ZoneId: {0}", ZoneId);
        }
    }
}
