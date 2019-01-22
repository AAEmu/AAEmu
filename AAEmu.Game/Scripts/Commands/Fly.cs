using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Fly : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("fly", this);
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Fly] /fly <true/false>");
                return;
            }

            if (bool.TryParse(args[0], out var isFlying))
                character.BroadcastPacket(new SCUnitFlyingStateChangedPacket(character.ObjId, isFlying), true);
            else
                character.SendMessage("[Fly] Throw parse bool!");
        }
    }
}