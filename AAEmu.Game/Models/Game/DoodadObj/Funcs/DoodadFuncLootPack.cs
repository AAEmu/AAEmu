using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncLootPack : DoodadFuncTemplate
    {
        public uint LootPackId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncLootPack : LootPackId {0}, SkillId {1}", LootPackId, skillId);
            Character character = (Character)caster;
            if (character == null) return;
            var lootPacks = ItemManager.Instance.GetLootPacks(LootPackId); 
            var dropRateMax = (uint)0;
            var items = new List<Item>();
            var groupNum = 0;
            var groupFound  = false;
            while (groupNum < 100)
            {
                groupNum += 1;
                _log.Warn("DoodadFuncLootPack : LootPackId {0}, SkillId {1} Group Num {2}", LootPackId, skillId, groupNum);
                groupFound = false;
                for (var ui = 0; ui < lootPacks.Length; ui++)
                {
                    if (lootPacks[ui].Group == groupNum)
                    {
                        dropRateMax += lootPacks[ui].DropRate;
                        groupFound = true;
                    }
                }
                var dropRateItem = Rand.Next(0, dropRateMax);
                var dropRateItemId = (uint)0;
                for (var uii = 0; uii < lootPacks.Length; uii++)
                {
                    if ((lootPacks[uii].DropRate + dropRateItemId >= dropRateItem) && (lootPacks[uii].Group == groupNum))
                    {
                        int count = Rand.Next(lootPacks[uii].MinAmount, lootPacks[uii].MaxAmount);
                        var item = ItemManager.Instance.Create(lootPacks[uii].ItemId, count, lootPacks[uii].GradeId);
                        InventoryHelper.AddItemAndUpdateClient(character, item);
                        break;
                    }
                    else
                    {
                        dropRateItemId += lootPacks[uii].DropRate;
                    }
                }
                if (groupFound == false)
                    break;
            }
        }
    }
}
