using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Utils.Scripts.SubCommands.Gold
{
    public class GoldChangeSubCommand : SubCommandBase
    {
        public GoldChangeSubCommand()
        {
            Title = "[Gold Change]";
            Description = "Changes to self or a player name or a selected target an specific amount of gold, silver and copper.";
            CallPrefix = "/item (add||change||remove)";
            AddParameter(new StringSubCommandParameter("player name||target||self", true));
            AddParameter(new NumericSubCommandParameter<int>("gold amount", true));
            AddParameter(new NumericSubCommandParameter<int>("silver amount", false));
            AddParameter(new NumericSubCommandParameter<int>("copper amount", false));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            Character targetCharacter;
            Character selfCharacter = (Character)character;

            var firstParameter = parameters["player name||target||self"].GetValue<string>();
            if (firstParameter == "target")
            {
                if ((selfCharacter.CurrentTarget is null) || !(selfCharacter.CurrentTarget is Character))
                {
                    SendColorMessage(character, Color.Red, "Please select a valid character player");
                    return;
                }
                targetCharacter = selfCharacter.CurrentTarget as Character;
            }
            else if (firstParameter == "self")
            {
                targetCharacter = selfCharacter;
            }
            else
            {
                Character player = WorldManager.Instance.GetCharacter(firstParameter);
                if (player is null)
                {
                    SendColorMessage(character, Color.Red, $"Character player: {firstParameter} was not found.");
                    return;
                }
                targetCharacter = player;
            }

            var silverAmount = 0;
            var copperAmount = 0;
            var multiplier = (triggerArgument == "remove") ? -1 : 1;
            var goldAmount = parameters["gold amount"].GetValue<int>();
            if (parameters.ContainsKey("silver amount"))
            {
                silverAmount = parameters["silver amount"].GetValue<int>();
            }
            if (parameters.ContainsKey("copper amount"))
            {
                copperAmount = parameters["copper amount"].GetValue<int>();
            }

            var totalAmount = (copperAmount * multiplier) + (silverAmount * 100 * multiplier) + (goldAmount * 10000 * multiplier);

            if (totalAmount != 0)
            {
                targetCharacter.Money += totalAmount;
                targetCharacter.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.AutoLootDoodadItem, new List<ItemTask> { new MoneyChange(totalAmount) }, new List<ulong>()));
                if (selfCharacter.Id != targetCharacter.Id)
                {
                    SendMessage(character, "Changed {0}'s money by {1}g {2}s {3}c", targetCharacter.Name, goldAmount, silverAmount, copperAmount);
                    SendMessage(targetCharacter, "[GM] {0} has changed your gold", selfCharacter.Name);
                }
            }
            else
            {
                SendColorMessage(character, Color.Red, "No valid amount sum provided");
            }
        }
    }
}
