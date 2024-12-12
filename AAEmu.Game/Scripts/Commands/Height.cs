using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Height : ICommand
{
    public string[] CommandNames { get; set; } = ["height"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target)";
    }

    public string GetCommandHelpText()
    {
        return "Gets your or target's current height and that of the supposed floor (using heightmap data)";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var targetPlayer = character;
        if (args.Length > 0)
        {
            targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstArg);
        }

        var height = WorldManager.Instance.GetHeight(targetPlayer.Transform.ZoneId,
            targetPlayer.Transform.World.Position.X, targetPlayer.Transform.World.Position.Y);
        CommandManager.SendNormalText(this, messageOutput,
            $"{targetPlayer.Name} Z-Pos: {character.Transform.World.Position.Z} - Floor: {height}");
    }
}
