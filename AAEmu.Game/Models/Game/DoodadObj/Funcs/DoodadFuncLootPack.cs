using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
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
            LootPacks[] lootPacks = ItemManager.Instance.GetLootPacks(LootPackId);
            Random itemQuantity = new Random();
            var count = 0;
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
                count = itemQuantity.Next(pack.MinAmount, pack.MaxAmount);
                character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.AutoLootDoodadItem, pack.ItemId, count);
            }

            var nextfunc = DoodadManager.Instance.GetFunc(owner.FuncGroupId, skillId);
            owner.FuncGroupId = (uint)nextfunc.NextPhase;
            nextfunc = DoodadManager.Instance.GetFunc(owner.FuncGroupId, nextfunc.SkillId);
            if (nextfunc != null)
            {
                _log.Debug("DoodadFuncLootPack is now calling " + nextfunc.FuncType);
                nextfunc.Use(caster, owner, skillId);
            }
            else //Doodad interaction is done, begin clean up
            {
                DoodadManager.Instance.TriggerPhaseFunc(GetType().Name, owner.FuncGroupId, caster, owner, skillId);
                //^ Notice the funcGroupID is already the next next phase
            }
        }
    }
}
