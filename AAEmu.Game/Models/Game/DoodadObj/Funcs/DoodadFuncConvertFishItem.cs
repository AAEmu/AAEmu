using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncConvertFishItem : DoodadFuncTemplate
    {
        public uint DoodadFuncConvertFishId { get; set; }
        public uint ItemId { get; set; }
        public uint LootPackId { get; set; }
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncConvertFishItem");
        }
    }
}
