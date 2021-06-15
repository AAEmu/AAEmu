using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

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
            var playerTarget = character.CurrentTarget;
            
            var chatTarget = args.Length > 0 ? args[0] : "";
            Character targetPlayer = WorldManager.Instance.GetCharacter(chatTarget);
            if ((chatTarget != String.Empty) && (targetPlayer != null))
                playerTarget = targetPlayer;
            
            if ((targetPlayer is Character) && (playerTarget != null))
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
            else
            if (playerTarget is Unit unit)
            {
                // Player is trying to heal some other unit
                if (unit.Hp == 0)
                {
                    character.SendMessage("Cannot heal a dead target");
                }
                else
                {
                    unit.Hp = unit.MaxHp;
                    unit.Mp = unit.MaxMp;
                    unit.BroadcastPacket(new SCUnitPointsPacket(unit.ObjId, unit.Hp, unit.Mp), true);
                    character.SendMessage("{0} => {1}/{2} HP, {3}/{4} MP",
                        unit.Name, unit.Hp, unit.MaxHp, unit.Mp, unit.MaxMp);
                }
            }
            
        }
    }
}
