using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command: clear a guild's war protection (post-war cooldown or Ceasefire item) immediately.
/// Usage: /endgp &lt;guild name&gt;   (name may contain spaces - the rest of the line is the name)
/// </summary>
public class EndGuildProtection : ICommand
{
    public string[] CommandNames { get; set; } = ["endgp", "endguildprotection"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<guild name>";
    }

    public string GetCommandHelpText()
    {
        return "Clears the named guild's war protection (post-war cooldown or Ceasefire item) right now.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var guildName = string.Join(' ', args).Trim();
        var resolved = ExpeditionManager.Instance.EndGuildProtection(guildName);
        if (resolved == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"No guild named \"{guildName}\".");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput, $"{resolved}'s war protection cleared.");
    }
}
