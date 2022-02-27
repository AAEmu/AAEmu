using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncLootPack : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint LootPackId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncLootPack : LootPackId {0}, SkillId {1}", LootPackId, skillId);

            var character = (Character)caster;
            if (character == null)
                return;

            var lootPacks = ItemManager.Instance.GetLootPacks(LootPackId);
            var itemQuantity = new Random();
            if (character.Inventory.Bag.FreeSlotCount >= lootPacks.Length)
            {
                foreach (var pack in lootPacks)
                {
                    //_log.Warn(pack.Id);
                    //_log.Warn(pack.Group);
                    //_log.Warn(pack.ItemId);
                    //_log.Warn(pack.DropRate);
                    //_log.Warn(pack.MinAmount);
                    //_log.Warn(pack.MaxAmount);
                    //_log.Warn(pack.LootPackId);
                    //_log.Warn(pack.GradeId);
                    //_log.Warn(pack.AlwaysDrop);

                    //TODO create dropRate chance
                    var count = itemQuantity.Next(pack.MinAmount, pack.MaxAmount);
                    character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.AutoLootDoodadItem, pack.ItemId, count);
                }
            }
            else
            {
                character.SendErrorMessage(ErrorMessageType.BagFull);
            }
        }
    }
}
