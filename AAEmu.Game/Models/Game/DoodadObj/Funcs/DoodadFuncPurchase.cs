using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPurchase : DoodadFuncTemplate
    {
        public uint ItemId { get; set; }
        public int Count { get; set; }
        public uint CoinItemId { get; set; }
        public int CoinCount { get; set; }
        public uint CurrencyId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncPurchase");
        }
    }
}
