using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestCombat : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("test_combat", this);
        }
        
        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[TestCombat] mods: engaged, cleared, first_hit");
                return;
            }

            switch (args[0])
            {
                case "engaged": // TODO Battle Start
                    if (character.CurrentTarget != null)
                    {
                        character.SendPacket(new SCCombatEngagedPacket(character.ObjId));
                        character.SendPacket(new SCCombatEngagedPacket(character.CurrentTarget.ObjId));
                    }
                    else
                        character.SendMessage("[TestCombat] not have target");

                    break;
                case "cleared": // TODO Battle End
                    if (character.CurrentTarget != null)
                    {
                        character.SendPacket(new SCCombatClearedPacket(character.ObjId));
                        character.SendPacket(new SCCombatClearedPacket(character.CurrentTarget.ObjId));
                    }
                    else
                        character.SendMessage("[TestCombat] not have target");

                    break;
                case "first_hit": 
                    if (character.CurrentTarget != null)
                        character.SendPacket(new SCCombatFirstHitPacket(character.ObjId, character.CurrentTarget.ObjId,
                            0));
                    else
                        character.SendMessage("[TestCombat] not have target");
                    break;
                case "text": // TODO Combat Effect
                    character.SendPacket(new SCCombatTextPacket(0, character.ObjId, 0));
                    break;
            }
        }
    }
}
