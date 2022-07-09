using System.Drawing;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils.Converters;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class ItemAddSubCommand : SubCommandBase 
    {
        public ItemAddSubCommand()
        {
            Prefix = "[Item]";
            Description = "Adds to self or a specified character or selected target an amount of a specific item template of a specific [grade].";
            CallExample = "/item add (<charactername>||target||self) [amount=1] [grade=0]";
        }

        public override void PreExecute(ICharacter character, string triggerArgument, string[] args)
        {
            Character addTarget;
            Character selfCharacter = (Character)character;
            if (args.Length < 2)
            {
                SendHelpMessage(character);
                return;
            }

            var firstArgument = args.First();
            if (firstArgument == "target")
            {
                if ((selfCharacter.CurrentTarget is  null) || !(selfCharacter.CurrentTarget is Character))
                {
                    SendColorMessage(character, Color.Red, "Please select a valid character player");
                    return;
                }
                addTarget = selfCharacter.CurrentTarget as Character;
            }
            else if (firstArgument == "self")
            {
                addTarget = selfCharacter;
            }
            else
            {
                Character player = WorldManager.Instance.GetCharacter(firstArgument);
                if (player is null)
                {
                    SendColorMessage(character, Color.Red, $"Character player: {firstArgument} was not found.");
                    return;
                }
                addTarget = player;
            }

            if (!uint.TryParse(args[1], out uint templateId))
            {
                SendColorMessage(character, Color.Red, "Item template id should be numeric");
                return;
            }

            var itemTemplate = ItemManager.Instance.GetTemplate(templateId);
            var itemAmount = 1; //Default amount 
            if (itemTemplate is null)
            {
                SendColorMessage(character, Color.Red, $"Item template id {templateId} does not exist!|r");
                return;
            }

            if (args.Length > 2 && (!int.TryParse(args[2], out itemAmount) || itemAmount <= 0))
            {
                SendColorMessage(character, Color.Red, "Item count id should be numeric and greater than 0");
                return;
            }

            byte itemGrade = 0;
            if (args.Length > 3) 
            {
                if (!byte.TryParse(args[3], out itemGrade))
                {
                    SendColorMessage(character, Color.Red, "Item grade should be numeric");
                    return;
                }

                if (itemGrade > (byte)ItemGrade.Mythic || itemGrade < (byte)ItemGrade.Crude)
                {
                    SendColorMessage(character, Color.Red, "Item grade cannot be lower than {0} or exceed {1}!|r", (byte)ItemGrade.Crude, (byte)ItemGrade.Mythic);
                    return;
                }
            }

            if (ItemManager.Instance.IsAutoEquipTradePack(itemTemplate.Id))// .Category_Id == 133) || (itemTemplate.Category_Id == 122)) // Speciality Packs or Tradepacks
            {
                var currentBackpack = addTarget.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
                if (currentBackpack != null)
                {
                    SendColorMessage(character, Color.Red, "No room on the backpack slot to place a tradepack!|r");
                    return;
                }
                if (!addTarget.Inventory.Equipment.AcquireDefaultItem(ItemTaskType.Gm, templateId, itemAmount, itemGrade))
                {
                    SendColorMessage(character, Color.Red, "Tradepack could not be created!|r");
                    return;
                }
            }
            else if (!addTarget.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Gm, templateId, itemAmount, itemGrade))
            {
                SendColorMessage(character, Color.Red, "Item could not be created!|r");
                return;
            }

            if (selfCharacter.Id != addTarget.Id)
            {
                SendMessage(character, $"Added item {ChatConverter.ConvertAsChatMessageReference(templateId, itemGrade)} to {addTarget.Name}'s inventory");
                SendMessage(addTarget, $"[GM] {selfCharacter.Name} added {ChatConverter.ConvertAsChatMessageReference(templateId, itemGrade)} to your inventory");
            }
        }
    }
}
