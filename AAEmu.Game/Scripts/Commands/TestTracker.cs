using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestTracker : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "testtracker", "track", "tt" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) [objId]";
        }

        public string GetCommandHelpText()
        {
            return "Toggle movement debug information for target";
        }

        public void Execute(Character character, string[] args)
        {
            var playerTarget = character.CurrentTarget;

            GameObject targetObject = character.CurrentTarget;
            if ((args.Length > 0) && (uint.TryParse(args[0], out var targetObjIdVal)))
                targetObject = WorldManager.Instance.GetGameObject(targetObjIdVal);

            if ((targetObject != null) && (targetObject.Transform != null))
            {
                character.SendMessage("[TestTracking] {0} tracking {1} - {2}",
                    targetObject.Transform.ToggleDebugTracker(character) ? "Now" : "No longer",
                    targetObject.ObjId,
                    (targetObject is BaseUnit bu) ? bu.Name : "<gameobject>");
            }
            else
            {
                character.SendMessage("[TestTracking] Invalid object");
            }
        }
    }
}
