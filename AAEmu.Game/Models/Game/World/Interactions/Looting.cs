using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class Looting : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
            uint skillId, uint ItemId, DoodadFuncTemplate objectFunc)
        {
            var character = (Character)caster;
            if (character == null) { return; }
            var chance = Rand.Next(0, 10000);
            if (!(objectFunc is DoodadFuncLootItem obj)) { return; }
            if (chance > obj.Percent) { return; }
            var count = Rand.Next(obj.CountMin, obj.CountMax);

            var item = ItemManager.Instance.Create(ItemId, count, 0);
            InventoryHelper.AddItemAndUpdateClient(character, item);
        }
    }
}
