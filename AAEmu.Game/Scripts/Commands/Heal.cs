using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Scripts.Commands
{
    public class Heal : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "heal" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) or self";
        }

        public string GetCommandHelpText()
        {
            return "Heals target or self if no target supplied";
        }

        public void Execute(Character character, string[] args)
        {
            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args.Length > 0 ? args[0] : null, out var _);
            var playerTarget = character.CurrentTarget;
            if (playerTarget is Npc)
            {
                // Player is trying to heal an NPC
                var npcChar = (Npc)character.CurrentTarget;
                if (npcChar.Hp == 0)
                {
                    character.SendMessage("Cannot heal a dead target");
                }
                else
                {
                    npcChar.Hp = npcChar.MaxHp;
                    npcChar.BroadcastPacket(new SCUnitPointsPacket(npcChar.ObjId, npcChar.Hp, npcChar.Mp), true);
                }
            }
            else if (targetPlayer is Character)
            {
                if (targetPlayer.Hp == 0)
                {
                    // This check is needed otherwise the player will be kicked
                    character.SendMessage("Cannot heal a dead target, use the revive command instead");
                }
                else
                {
                    targetPlayer.Hp = targetPlayer.MaxHp;
                    targetPlayer.Mp = targetPlayer.MaxMp;
                    targetPlayer.BroadcastPacket(new SCUnitPointsPacket(targetPlayer.ObjId, targetPlayer.Hp, targetPlayer.Mp), true);
                }
            }
        }
    }
}
