﻿using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncLootItem : DoodadFuncTemplate
    {
        public WorldInteractionType WorldInteractionId { get; set; }
        public uint ItemId { get; set; }
        public int CountMin { get; set; }
        public int CountMax { get; set; }
        public int Percent { get; set; }
        public int RemainTime { get; set; }
        public uint GroupId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            owner.NeedChangePhase = false;

            var character = (Character)caster;
            if (character == null)
                return;

            var chance = Rand.Next(0, 10000);
            if (chance > Percent)
                return;

            var count = Rand.Next(CountMin, CountMax);

            if (ItemManager.Instance.IsAutoEquipTradePack(ItemId))
            {
                var item = ItemManager.Instance.Create(ItemId, count, 0);
                if (character.Inventory.TakeoffBackpack(ItemTaskType.RecoverDoodadItem, true))
                {
                    if (character.Inventory.Equipment.AddOrMoveExistingItem(ItemTaskType.RecoverDoodadItem, item, (int)Items.EquipmentItemSlot.Backpack))
                    {
                        owner.NeedChangePhase = true;
                    }
                }
            }
            else
            {
                owner.NeedChangePhase = character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.RecoverDoodadItem, ItemId, count);
            }

            if (owner.NeedChangePhase == false)
                character.SendErrorMessage(ErrorMessageType.BagFull);

            //// попытка исправить переполнение инвентаря по квесту Id=259, Echoes from the Past, Solzreed Peninsula, Solzreed Peninsula
            //if (character.Inventory.GetItemsCount(ItemId) == count)
            //{
            //    owner.NeedChangePhase = false;
            //    character.SendErrorMessage(ErrorMessageType.ItemPickupLimit);
            //    return;
            //}

            //if (character.Inventory.TryAddNewItem(ItemTaskType.AutoLootDoodadItem, ItemId, count))
            //    owner.NeedChangePhase = true;
            //else
            //    character.SendErrorMessage(ErrorMessageType.BagFull);
        }
    }
}
