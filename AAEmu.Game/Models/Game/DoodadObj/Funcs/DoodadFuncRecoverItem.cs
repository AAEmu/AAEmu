using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRecoverItem : DoodadFuncTemplate
    {
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncRecoverItem");

            //TODO: itemId currently using itemtemplate but shouldn't, needs to retain original crafter 
            var character = (Character)caster;
            character.Inventory.Equipment.AcquireDefaultItem(Items.Actions.ItemTaskType.CraftPickupProduct, (uint)owner.ItemId, 1);
            owner.Delete();
        }
    }
}
