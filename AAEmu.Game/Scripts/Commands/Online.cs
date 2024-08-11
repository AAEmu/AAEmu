using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Online : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "online", "list_online" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[filter]";
    }

    public string GetCommandHelpText()
    {
        return
            "Lists the number of online players. If a filter or * is provided, it will be used as a filter to show up to 100 names as well.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var characters = WorldManager.Instance.GetAllCharacters();

        if (args.Length > 0)
        {
            var finalMessage = string.Empty;
            var filter = args[0];
            var showCount = 0;
            foreach (var onlineCharacter in characters)
            {
                if (!onlineCharacter.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) && filter != "*")
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(finalMessage))
                {
                    finalMessage += ", ";
                }

                var factionColor = character.Faction.GetRelationState(onlineCharacter.Faction) switch
                {
                    RelationState.Hostile => "FFFF4444",
                    RelationState.Neutral => "FF888888",
                    RelationState.Friendly => "FF4444FF",
                    _ => "FFFFFFFF"
                };
                if (onlineCharacter.Id == character.Id)
                {
                    factionColor = "FFFFFFFF";
                }

                finalMessage += $"|c{factionColor}{onlineCharacter.Name}|r";
                showCount++;
                if (showCount > 100)
                {
                    break;
                }
            }

            messageOutput.SendMessage(finalMessage);
            CommandManager.SendNormalText(this, messageOutput,
                showCount != characters.Count
                    ? $"Showing {showCount}/{characters.Count} online players."
                    : $"{characters.Count} players online."
            );
        }
        else
        {
            CommandManager.SendNormalText(this, messageOutput, $"{characters.Count} players online.");
        }
    }
}
