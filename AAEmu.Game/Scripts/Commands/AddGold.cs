using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddGold : ICommand
{
    public string[] CommandNames { get; set; } = ["gold", "addgold", "add_gold"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <gold> [silver] [copper]>";
    }

    public string GetCommandHelpText()
    {
        return "Adds X amount of money to target (can be negative values).";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstarg);

        var argGold = 0;
        var argSilver = 0;
        var argCopper = 0;

        if (args.Length > firstarg && int.TryParse(args[firstarg], out var amount))
        {
            argGold = amount;
        }

        if (args.Length > firstarg + 1 && int.TryParse(args[firstarg + 1], out amount))
        {
            argSilver = amount;
        }

        if (args.Length > firstarg + 2 && int.TryParse(args[firstarg + 2], out amount))
        {
            argCopper = amount;
        }

        var argTotal = argCopper + argSilver * 100 + argGold * 10000;

        if (argTotal != 0)
        {
            targetPlayer.Money += argTotal;
            targetPlayer.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.AutoLootDoodadItem,
                [new MoneyChange(argTotal)], []));
            if (character.Id != targetPlayer.Id)
            {
                CommandManager.SendNormalText(this, messageOutput,
                    $"changed {targetPlayer.Name}'s money by {argGold}g {argSilver}s {argCopper}c");
                targetPlayer.SendMessage($"[GM] {character.Name} has adjusted your money");
            }
        }
        else
        {
            CommandManager.SendErrorText(this, messageOutput, "No valid amount provided ...");
        }
    }
}
