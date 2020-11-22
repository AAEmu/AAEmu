using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using Microsoft.CodeAnalysis.Text;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPurchase : DoodadFuncTemplate
    {
        public uint ItemId { get; set; }
        public int Count { get; set; }
        public uint CoinItemId { get; set; }
        public int CoinCount { get; set; }
        public uint CurrencyId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if (!(caster is Character character))
                return;

            if (character.Inventory.Bag.SpaceLeftForItem(ItemId) < Count)
            {
                character.SendErrorMessage(Error.ErrorMessageType.BagFull);
                return;
            }

            if (character.Inventory.Bag.ConsumeItem(ItemTaskType.DoodadInteraction, CoinItemId, CoinCount, null) <= 0)
            {
                character.SendErrorMessage(Error.ErrorMessageType.NotEnoughItem);
                return;
            }

            if (ItemManager.Instance.IsAutoEquipTradePack(ItemId))
            {
                if (!character.Inventory.TryEquipNewBackPack(ItemTaskType.QuestSupplyItems, ItemId, Count))
                {
                    _log.Error(string.Format("DoodadFuncPurchase: Failed to auto-equip backpack item {0} for player {1}", ItemId, character.Name));
                    character.SendErrorMessage(Error.ErrorMessageType.BackpackOccupied);
                    return;
                }
            }
            else
            {
                if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.DoodadInteraction, ItemId, Count))
                {
                    _log.Error(string.Format("DoodadFuncPurchase: Failed to create item {0} for player {1}", ItemId, character.Name));
                    return;
                }
            }

        }
    }
}
