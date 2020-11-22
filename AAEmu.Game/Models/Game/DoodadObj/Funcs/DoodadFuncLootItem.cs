using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
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

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            Character character = (Character)caster;
            if (character == null) return;

            int chance = Rand.Next(0, 10000);
            if (chance > Percent) return;

            int count = Rand.Next(CountMin, CountMax);
            if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.AutoLootDoodadItem, ItemId, count))
                character.SendErrorMessage(Error.ErrorMessageType.BagFull);
            // else
            // {
            //     character.SendErrorMessage(Error.ErrorMessageType.BagFull);
            // }
        }
    }
}
