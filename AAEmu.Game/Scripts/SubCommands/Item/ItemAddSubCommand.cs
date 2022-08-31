using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils.Converters;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class ItemAddSubCommand : SubCommandBase 
    {
        public ItemAddSubCommand()
        {
            Title = "[Item]";
            Description = "Adds to self or a player name or a selected target an amount of a specific item template of a specific [grade].";
            CallPrefix = $"{CommandManager.CommandPrefix}item add";
            AddParameter(new StringSubCommandParameter("target", "player name||target||self", true));
            AddParameter(new NumericSubCommandParameter<uint>("templateId", "template id", true));
            AddParameter(new NumericSubCommandParameter<int>("amount", "amount=1", false, 1, 1000) { DefaultValue = 1 });
            AddParameter(new NumericSubCommandParameter<byte>("grade", "item grade=0", false, (byte)ItemGrade.Crude, (byte)ItemGrade.Mythic) { DefaultValue = (byte)ItemGrade.Crude });
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            Character addTarget;
            Character selfCharacter = (Character)character;

            string firstArgument = parameters["target"];
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
                    SendColorMessage(character, Color.Red, $"Player: {firstArgument} was not found.");
                    return;
                }
                addTarget = player;
            }

            uint templateId = parameters["templateId"];
            int itemAmount = parameters["amount"];
            byte itemGrade = parameters["grade"];

            var itemTemplate = ItemManager.Instance.GetTemplate(templateId);
            if (itemTemplate is null)
            {
                SendColorMessage(character, Color.Red, $"Item template id {templateId} does not exist!|r");
                return;
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
