using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command to add exp to the caller's own guild, auto-advancing its level per expedition_levels.
/// </summary>
public class GuildExp : ICommand
{
    public string[] CommandNames { get; set; } = ["guildexp", "expedition_exp"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<amount>";
    }

    public string GetCommandHelpText()
    {
        return "Adds exp to your own guild, auto-advancing its level per expedition_levels.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.Expedition == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "You are not in a guild.");
            return;
        }

        if (args.Length == 0 || !uint.TryParse(args[0], out var amount) || amount == 0)
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: /guildexp <amount>");
            return;
        }

        ExpeditionManager.Instance.AddExp(character.Expedition, amount);
        CommandManager.SendNormalText(this, messageOutput,
            $"{character.Expedition.Name} is now level {character.Expedition.Level} ({character.Expedition.Exp} exp).");
    }
}
