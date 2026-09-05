using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command to stop the caller's guild's current war immediately.
///   /gwend        - normal end: applies rewards + the 48h re-declaration protection (like a real timeout)
///   /gwend wipe   - clears the war state on both guilds with NO rewards and NO protection, so a fresh
///                   war can be declared right away (for testing)
/// </summary>
public class GuildWarEnd : ICommand
{
    public string[] CommandNames { get; set; } = ["gwend", "guildwarend"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[wipe]";
    }

    public string GetCommandHelpText()
    {
        return "Ends your guild's current war now. Add 'wipe' to clear it with no rewards/protection so you can re-declare immediately.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var expedition = character.Expedition;
        if (expedition == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"{character.Name} is not in a guild.");
            return;
        }

        if (!expedition.IsAtWar && !expedition.IsProtected)
        {
            CommandManager.SendErrorText(this, messageOutput, $"{expedition.Name} is not at war or protected.");
            return;
        }

        var wipe = args.Length > 0 && args[0].Equals("wipe", System.StringComparison.OrdinalIgnoreCase);

        if (wipe)
        {
            ExpeditionManager.Instance.WipeWar(expedition.Id);
            CommandManager.SendNormalText(this, messageOutput, $"{expedition.Name}'s war state wiped (no rewards, no protection) - you can declare again now.");
        }
        else
        {
            ExpeditionManager.Instance.EndWar(expedition.Id);
            CommandManager.SendNormalText(this, messageOutput, $"{expedition.Name}'s war ended now (rewards paid, 48h protection applied).");
        }
    }
}
