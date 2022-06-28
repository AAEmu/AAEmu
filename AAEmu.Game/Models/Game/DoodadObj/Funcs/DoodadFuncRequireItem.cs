using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRequireItem : DoodadPhaseFuncTemplate
    {
        public WorldInteractionType WorldInteractionId { get; set; }
        public uint ItemId { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncRequireItem");
            if (caster is Character character)
            {
                //character.Quests.OnInteraction(WorldInteractionId, character.CurrentTarget);
                if (character.Inventory.GetItemsCount(ItemId) > 0)
                    return false; // продолжим выполнение, подходящий квест и есть нужный предмет
                else
                    return true; // прерываем, не подходящий квест и нет нужного предмета
            }
            return true; // прерываем, не подходящий квест и нет нужного предмета
        }
    }
}
