using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class Fly : ICommand
    {
        private static List<uint> characterFlyStateCache = new List<uint>();

        public void OnLoad()
        {
            CommandManager.Instance.Register("fly", this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) [true||false]";
        }

        public string GetCommandHelpText()
        {
            return "Enables or disables fly-mode (also makes you move at hi-speed)";
        }

        public void Execute(Character character, string[] args)
        {
            var targetPlayer = character;

            var firstArg = 0;
            if (args.Length > 0)
                targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out firstArg);

            var isFlying = !characterFlyStateCache.Contains(targetPlayer.Id);
            if (args.Length > firstArg)
            {
                if (!bool.TryParse(args[firstArg + 0], out isFlying))
                {
                    character.SendMessage("|cFFFF0000[Fly] Bool parse error!|r");
                    return;
                }
            }

            targetPlayer.SendPacket(new SCUnitFlyingStateChangedPacket(targetPlayer.ObjId, isFlying));
            targetPlayer.SendMessage($"[Fly] State changed to |cFFFFFFFF{isFlying}|r");
            if (isFlying)
                characterFlyStateCache.Add(targetPlayer.Id);
            else
                characterFlyStateCache.Remove(targetPlayer.Id);
        }
    }
}
