using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM: wipe the caller's current guild (disband if owner, leave otherwise) and found a
/// fresh one-member guild. Bypasses the normal party-of-5 + gold create gates.
/// </summary>
public class GuildCreate : ICommand
{
    public string[] CommandNames { get; set; } = ["guildcreate", "createguild"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<name>";
    }

    public string GetCommandHelpText()
    {
        return "Disbands (or leaves) your current guild, then creates a new solo guild you own.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: /guildcreate <name>");
            return;
        }

        // Name may contain spaces - rejoin the remainder of the line.
        var name = string.Join(' ', args).Trim();

        var current = character.Expedition;
        if (current != null)
        {
            if (current.OwnerId == character.Id)
            {
                if (!ExpeditionManager.Instance.Disband(character))
                {
                    CommandManager.SendErrorText(this, messageOutput, "Failed to disband the current guild.");
                    return;
                }
                CommandManager.SendNormalText(this, messageOutput, $"Disbanded {current.Name}.");
            }
            else
            {
                ExpeditionManager.Instance.Leave(character);
                if (character.Expedition != null)
                {
                    CommandManager.SendErrorText(this, messageOutput, "Failed to leave the current guild.");
                    return;
                }
                CommandManager.SendNormalText(this, messageOutput, $"Left {current.Name}.");
            }
        }

        if (!ExpeditionManager.Instance.TryCreateSoloExpedition(character, name, out var error))
        {
            CommandManager.SendErrorText(this, messageOutput, error ?? "Failed to create guild.");
            return;
        }

        var guild = character.Expedition;
        CommandManager.SendNormalText(this, messageOutput,
            $"Created guild {guild.Name} (id {(uint)guild.Id}) as owner. You are the only member.");
    }
}
