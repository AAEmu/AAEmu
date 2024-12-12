using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Help : ICommand
{
    public string[] CommandNames { get; set; } = ["help", "?"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[topic]";
    }

    public string GetCommandHelpText()
    {
        return
            "Displays help about a command <topic>. If no <topic> is provided, a list of all GM commands will be displayed";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length > 0)
        {
            var thisCommand = args[0].ToLower();
            if (AccessLevelManager.Instance.GetLevel(CommandManager.Instance.GetCommandNameBase(thisCommand)) >
                character.AccessLevel)
            {
                // deliberately the same error as command not found 
                character.SendMessage("Help for: |cFFFFFFFF" + CommandManager.CommandPrefix + thisCommand +
                                      "|r not available!");
            }
            else
            {
                var cmd = CommandManager.Instance.GetCommandInterfaceByName(thisCommand);
                if (cmd == null)
                {
                    // deliberately the same error as insufficient rights 
                    character.SendMessage("Help for: |cFFFFFFFF" + CommandManager.CommandPrefix + thisCommand +
                                          "|r not available!");
                    return;
                }

                var helpLineText = cmd.GetCommandLineHelp();
                var helpText = cmd.GetCommandHelpText();
                character.SendMessage("Help for: |cFFFFFFFF" + CommandManager.CommandPrefix + thisCommand + " " +
                                      helpLineText + "|r\n|cFF999999" + helpText + "|r");
            }

            return;
        }

        character.SendMessage("|cFF80FFFFList of available GM Commands|r\n-------------------------\n");
        var list = CommandManager.Instance.GetCommandKeys();
        list.Sort();
        var characterAccessLevel = CharacterManager.Instance.GetEffectiveAccessLevel(character);
        foreach (var command in list)
        {
            if (command == "help")
            {
                continue;
            }

            if (AccessLevelManager.Instance.GetLevel(command) > characterAccessLevel)
            {
                continue;
            }

            var cmd = CommandManager.Instance.GetCommandInterfaceByName(command);
            if (cmd == null)
            {
                continue; // should never happen
            }

            var helpLineText = cmd.GetCommandLineHelp();
            if (helpLineText != string.Empty)
            {
                character.SendMessage(CommandManager.CommandPrefix + command + " |cFF999999" + helpLineText + "|r");
            }
            else
            {
                character.SendMessage(CommandManager.CommandPrefix + command);
            }
        }
    }
}
