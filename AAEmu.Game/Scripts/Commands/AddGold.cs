using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddGold : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "addgold", "add_gold" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <gold> [silver] [copper]>";
        }

        public string GetCommandHelpText()
        {
            return "Adds X amount of money to target (can be negative values).";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Gold] "+ CommandManager.CommandPrefix + "addgold (target) <gold> [silver] [copper]");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            var argGold = 0;
            var argSilver = 0;
            var argCopper = 0;
            var amount = 0;

            if ((args.Length > firstarg) && (int.TryParse(args[firstarg], out amount)))
            {
                argGold = amount;
            }
            if ((args.Length > firstarg+1) && (int.TryParse(args[firstarg+1], out amount)))
            {
                argSilver = amount;
            }
            if ((args.Length > firstarg + 2) && (int.TryParse(args[firstarg+2], out amount)))
            {
                argCopper = amount;
            }

            var argTotal = argCopper + (argSilver * 100) + (argGold * 10000);

            if (argTotal != 0)
            {
                targetPlayer.Money += argTotal;
                targetPlayer.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.AutoLootDoodadItem, new List<ItemTask> { new MoneyChange(argTotal) }, new List<ulong>()));
                if (character.Id != targetPlayer.Id)
                {
                    character.SendMessage("[Gold] changed {0}'s money by {1}g {2}s {3}c",targetPlayer.Name,argGold,argSilver,argCopper);
                    targetPlayer.SendMessage("[GM] {0} has adjusted your money", character.Name);
                }
            }
            else
                character.SendMessage("[Gold] No valid amount provided ...");
        }
    }
}
