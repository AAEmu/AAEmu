using AAEmu.Commons.Utils;
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
            owner.ToPhaseAndUse = false;

            var character = (Character)caster;
            if (character == null)
                return;

            var chance = Rand.Next(0, 10000);
            if (chance > Percent)
                return;

            var count = Rand.Next(CountMin, CountMax);
            if (character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.AutoLootDoodadItem, ItemId, count))
                owner.ToPhaseAndUse = true;
            else
                character.SendErrorMessage(Error.ErrorMessageType.BagFull);
        }
    }
}
