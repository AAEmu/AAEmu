using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestAI : ICommand
    {
        public void OnLoad()
        {
            string[] name = new string[] { "testai", "ai" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Forces the HoldPosition AI to the target.";
        }

        public void Execute(Character character, string[] args)
        {
            if (character.CurrentTarget != null && character.CurrentTarget is Npc npc)
            {
                npc.Patrol = null;
                npc.Ai = new AlmightyNpcAiCharacter() { Owner = npc, IdlePosition = npc.Transform.CloneDetached() };
                AIManager.Instance.AddAi(npc.Ai);
                return;
            }

            character.SendMessage("[TestAI] An NPC is not targetted.");
        }
    }
}
