using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncLootItem : DoodadFuncTemplate
    {
        public uint WorldInteractionId { get; set; }
        public uint ItemId { get; set; }
        public int CountMin { get; set; }
        public int CountMax { get; set; }
        public int Percent { get; set; }
        public int RemainTime { get; set; }
        public uint GroupId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            Character character = (Character) caster;
            if (character == null) return;
            
            int chance = Rand.Next(0, 10000);
            if (chance > Percent) return;

            int count = Rand.Next(CountMin, CountMax);
            
            Item item = ItemManager.Instance.Create(ItemId, count, 0);
            InventoryHelper.AddItemAndUpdateClient(character, item);
            
            _log.Debug("DoodadFuncLootItem");
        }
    }
}
