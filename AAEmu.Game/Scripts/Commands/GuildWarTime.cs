using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command: override the guild-war duration for testing. Affects wars declared AFTER it is set;
/// 0 restores the config value (expedition_war_duration, 1h on retail).
/// </summary>
public class GuildWarTime : ICommand
{
    public string[] CommandNames { get; set; } = ["gwtime", "guildwartime"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<minutes>";
    }

    public string GetCommandHelpText()
    {
        return "Set the guild-war duration (minutes) for wars declared from now on. 0 = use the config value (1h).";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0 || !int.TryParse(args[0], out var minutes) || minutes < 0)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"Guild-war duration override is currently {(ExpeditionManager.WarDurationTestMinutes > 0 ? ExpeditionManager.WarDurationTestMinutes + " min" : "off (config value)")}. Usage: /gwtime <minutes>  (0 = off)");
            return;
        }

        ExpeditionManager.WarDurationTestMinutes = minutes;
        CommandManager.SendNormalText(this, messageOutput,
            minutes > 0
                ? $"Guild-war duration override set to {minutes} minute(s) - applies to wars declared from now on."
                : "Guild-war duration override cleared - using the config value (1h).");
    }
}
