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
            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args.Length > 0 ? args[0] : null, out var _);
            var playerTarget = character.CurrentTarget;
            if (playerTarget is Npc)
            {
                // Player is trying to heal an NPC
                var npcChar = (Npc)character.CurrentTarget;
                var lootDropItems = ItemManager.Instance.CreateLootDropItems(npcChar.ObjId);
                if (lootDropItems.Count > 0)
                {
                    character.BroadcastPacket(new SCLootableStatePacket(npcChar.ObjId, true), true);
                }
                npcChar.DoDie(character);
            }
            else if (playerTarget is Character)
            {
                targetPlayer.DoDie(character);
            }
            else
            {
                character.SendMessage("Cannot kill this target");
            }
        }
    }
}
