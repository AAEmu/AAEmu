using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestTracker : ICommand
{
    public string[] CommandNames { get; set; } = ["testtracker", "track", "tt"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) [objId]";
    }

    public string GetCommandHelpText()
    {
        return "Toggle movement debug information for target";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var playerTarget = character.CurrentTarget;

        GameObject targetObject = character.CurrentTarget;
        if (args.Length > 0 && uint.TryParse(args[0], out var targetObjIdVal))
        {
            targetObject = WorldManager.Instance.GetGameObject(targetObjIdVal);
        }

        if (targetObject != null && targetObject.Transform != null)
        {
            var toggleResult = targetObject.Transform.ToggleDebugTracker(character) ? "Now" : "No longer";
            var unitName = targetObject is BaseUnit bu ? bu.Name : "<gameobject>";
            character.SendMessage($"[TestTracking] {toggleResult} tracking {targetObject.ObjId} - {unitName}");
        }
        else
        {
            character.SendMessage("[TestTracking] Invalid object");
        }
    }
}
