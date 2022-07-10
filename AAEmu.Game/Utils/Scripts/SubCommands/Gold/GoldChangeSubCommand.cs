using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
            Prefix = "[Gold Change]";
            Description = "Changes to self or a player name or a selected target an specific amount of gold, silver and copper.";
            CallExample = "/item (add||change||remove) (<charactername>||target||self) <gold amount> [<silver amount>] [<copper amount>]";
            AddParameter(new StringSubCommandParameter("target", true));
            AddParameter(new NumericSubCommandParameter<int>("goldAmount", true));
            AddParameter(new NumericSubCommandParameter<int>("silverAmount", false));
            AddParameter(new NumericSubCommandParameter<int>("copperAmount", false));
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            Character targetCharacter;
            Character selfCharacter = (Character)character;
            if (args.Length < 2)
            {
                SendHelpMessage(character);
                return;
            }

            var firstArgument = args.First();
            if (firstArgument == "target")
            {
                if ((selfCharacter.CurrentTarget is null) || !(selfCharacter.CurrentTarget is Character))
                {
                    SendColorMessage(character, Color.Red, "Please select a valid character player");
                    return;
                }
                targetCharacter = selfCharacter.CurrentTarget as Character;
            }
            else if (firstArgument == "self")
            {
                targetCharacter = selfCharacter;
            }
            else
            {
                Character player = WorldManager.Instance.GetCharacter(firstArgument);
                if (player is null)
                {
                    SendColorMessage(character, Color.Red, $"Character player: {firstArgument} was not found.");
                    return;
                }
                targetCharacter = player;
            }

            var silverAmount = 0;
            var copperAmount = 0;
            var multiplier = (triggerArgument == "remove") ? -1 : 1;
            if (!int.TryParse(args[1], out var goldAmount))
            {
                SendColorMessage(character, Color.Red, "Gold amount should be numeric");
                return;
            }

            if (args.Length > 2 && !int.TryParse(args[2], out silverAmount))
            {
                SendColorMessage(character, Color.Red, "Silver amount should be numeric");
                return;
            }
            if (args.Length > 3 && !int.TryParse(args[3], out copperAmount))
            {
                SendColorMessage(character, Color.Red, "Copper amount should be numeric");
                return;
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
                SendMessage(character, "No valid amount sum provided ...");
            }
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            Character targetCharacter;
            Character selfCharacter = (Character)character;

            var firstArgument = parameters["target"].Value.ToString();

            if (firstArgument == "target")
            {
                if ((selfCharacter.CurrentTarget is null) || !(selfCharacter.CurrentTarget is Character))
                {
                    SendColorMessage(character, Color.Red, "Please select a valid character player");
                    return;
                }
                targetCharacter = selfCharacter.CurrentTarget as Character;
            }
            else if (firstArgument == "self")
            {
                targetCharacter = selfCharacter;
            }
            else
            {
                Character player = WorldManager.Instance.GetCharacter(firstArgument);
                if (player is null)
                {
                    SendColorMessage(character, Color.Red, $"Character player: {firstArgument} was not found.");
                    return;
                }
                targetCharacter = player;
            }

            var multiplier = (triggerArgument == "remove") ? -1 : 1;
            var goldAmount = (int)parameters["goldAmount"].Value;
            var silverAmount = 0;
            var copperAmount = 0;

            if (parameters.TryGetValue("silverAmount", out var silverParameter))
            {
                silverAmount = (int)silverParameter.Value;
            }
            if (parameters.TryGetValue("copperAmount", out var copperParameter))
            {
                silverAmount = (int)copperParameter.Value;
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
                SendMessage(character, "No valid amount sum provided ...");
            }
            
        }
    }
}
