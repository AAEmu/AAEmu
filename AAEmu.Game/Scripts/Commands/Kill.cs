using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class Kill : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "kill" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target)";
        }

        public string GetCommandHelpText()
        {
            return "Kills target";
        }

        public void Execute(Character character, string[] args)
        {
            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, null, out var _);
            var playerTarget = character.CurrentTarget;
            if (playerTarget is Npc)
            {
                // Player is trying to kill an NPC/Monster
                var npcChar = (Npc)character.CurrentTarget;
                if (npcChar.Hp == 0)
                {
                    character.SendMessage("Target is already dead");
                }
                else
                {
                    // We must broadcast this package because if character had initially attacked the mob and then executed kill
                    // the mob's "ghost" will still be attacking you and draining HP even though he doesn't exist in the world anymore
                    npcChar.CurrentTarget = null;
                    // HP must be set to 0 because if character engaged in battle and then ran kill command, after mob dies
                    // its hp will start regenerating
                    npcChar.Hp = 0;
                    npcChar.DoDie(character);
                }
            }
            else if (playerTarget is Character)
            {
                if (targetPlayer.Hp == 0)
                {
                    character.SendMessage("Target is already dead");
                }
                else
                {
                    targetPlayer.DoDie(character);
                }
            }
            else
            {
                character.SendMessage("Cannot kill this target");
            }
            character.IsInBattle = false; // In case the character gets stuck in battle mode after engaging a mob
            character.BroadcastPacket(new SCCombatClearedPacket(character.ObjId), true);
        }
    }
}
