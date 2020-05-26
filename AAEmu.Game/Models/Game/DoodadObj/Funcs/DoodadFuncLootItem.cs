using AAEmu.Commons.Utils;
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
            
            Item item = ItemManager.Instance.Create(ItemManager.Instance.GetTemplate(ItemId).Id, count, 0);
            character.Inventory.AddItem(ItemTaskType.AutoLootDoodadItem, item);

            var nextfunc = DoodadManager.Instance.GetFunc(owner.FuncGroupId, skillId);
            owner.FuncGroupId = (uint)nextfunc.NextPhase;
            nextfunc = DoodadManager.Instance.GetFunc(owner.FuncGroupId, nextfunc.SkillId);

            if (nextfunc != null)
            {
                _log.Warn("DoodadFuncLootItem is now calling " + nextfunc.FuncType);
                nextfunc.Use(caster, owner, skillId);
            }
            else //Doodad interaction is done, begin clean up
            {
                DoodadManager.Instance.TriggerPhaseFunc(GetType().Name, owner.FuncGroupId, caster, owner, skillId); 
                //^ Notice the funcGroupID is already the next -> next phase
            }
        }
    }
}
