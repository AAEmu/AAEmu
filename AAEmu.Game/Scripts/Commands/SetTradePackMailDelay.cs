using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Models;

namespace AAEmu.Game.Scripts.Commands;

public class SetTradePackMailDelay : ICommand
{
    public string[] CommandNames { get; set; } = ["settradepackmaildelay"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<delay_in_minutes>";
    }

    public string GetCommandHelpText()
    {
        return $"Changes the delay in receiving the trade pack reward mail to <delay> minute(s)\n" +
               $"Example usage:\n" +
               $"Receive the reward without delay: {CommandManager.CommandPrefix} {CommandNames[0]} 0\n" +
               $"Receive the reward in five minutes: {CommandManager.CommandPrefix} {CommandNames[0]} 5\n" +
               $"Receive the reward in 4 hours: {CommandManager.CommandPrefix} {CommandNames[0]} 240\n";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (double.TryParse(args.First(), out var delay))
        {
            AppConfiguration.Instance.Specialty.TradePackMailDelayInMinutes = delay;
            CommandManager.SendNormalText(this, messageOutput, $"Trade Pack Mail Delay set to {delay} minute(s)");
            return;
        }

        CommandManager.SendErrorText(this, messageOutput, $"Invalid delay: {args.First()}");
    }
}
