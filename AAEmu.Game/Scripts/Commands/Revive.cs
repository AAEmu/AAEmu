using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Revive : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "revive" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target)";
        }

        public string GetCommandHelpText()
        {
            return "Revives target.";
        }

        public void Execute(Character character, string[] args)
        {
            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args.Length > 0 ? args[0] : null, out var _);
            if (targetPlayer == null)
            {
                character.SendMessage("Cannot revive this target");
                return;
            }
            if(targetPlayer.Hp != 0)
            {
                character.SendMessage("Target is already alive");
                return;
            }

            targetPlayer.Hp = targetPlayer.MaxHp;
            targetPlayer.Mp = targetPlayer.MaxMp;

            var pos = targetPlayer.Transform.World.Position;
            targetPlayer.BroadcastPacket(new SCCharacterResurrectedPacket(targetPlayer.ObjId, pos.X, pos.Y, pos.Z, targetPlayer.Transform.World.Rotation.Z), true);
            targetPlayer.BroadcastPacket(new SCUnitPointsPacket(targetPlayer.ObjId, targetPlayer.Hp, targetPlayer.Mp), true);
        }
    }
}
