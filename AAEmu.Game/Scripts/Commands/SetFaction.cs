using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class SetFaction : ICommand
{
    public string[] CommandNames { get; set; } = ["setfaction", "set_faction"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<faction name or id>";
    }

    public string GetCommandHelpText()
    {
        return "Gets or sets selected target's faction";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.CurrentTarget is not Unit targetUnit)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (args.Length == 0)
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Current target is from faction: {targetUnit.Faction?.Name} ({targetUnit.Faction?.Id})");
            // CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (!Enum.TryParse<FactionsEnum>(args[0], true, out var faction))
        {
            faction = FactionsEnum.Invalid;
        }

        if (faction == FactionsEnum.Invalid)
        {
            CommandManager.SendErrorText(this, messageOutput, $"Invalid faction name or number {args[0]}");
            return;
        }

        if (FactionManager.Instance.GetFaction(faction) == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"{args[0]} is not a valid faction");
            return;
        }

        character.SetFaction(faction);
        CommandManager.SendNormalText(this, messageOutput, $"Faction set to: {faction} ({(uint)faction})");
    }
}
