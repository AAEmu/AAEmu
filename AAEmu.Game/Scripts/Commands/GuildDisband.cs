using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class GuildDisband : ICommand
{
    public string[] CommandNames { get; set; } = ["guilddisband", "disbandguild"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "";

    public string GetCommandHelpText() => "Disbands your guild if you are the owner (GM wipe helper).";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var guild = character.Expedition;
        if (guild == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "You are not in a guild.");
            return;
        }

        var name = guild.Name;
        if (!ExpeditionManager.Instance.Disband(character))
        {
            CommandManager.SendErrorText(this, messageOutput, "Failed to disband (must be owner).");
            return;
        }

        ExpeditionManager.Instance.ClearExpeditionRejoinCooldown(character);
        CommandManager.SendNormalText(this, messageOutput,
            $"Disbanded {name}. Rejoin cooldown cleared (memory and saved).");
    }
}
