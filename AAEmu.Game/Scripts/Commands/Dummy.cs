using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.Commands
{
    public class Dummy : ICommand
    {
        private const uint DUMMY_NPC_TEMPLATE_ID = 7512;
        
        public void OnLoad()
        {
            string[] name = { "dummy", "scarecrow" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Spawns a training dummy with no AI";
        }

        public void Execute(Character character, string[] args)
        {
            float newX;
            float newY;
            double angle;
            sbyte newRotZ;
            
            if (!NpcManager.Instance.Exist(DUMMY_NPC_TEMPLATE_ID))
            {
                character.SendMessage("|cFFFF0000[Dummy] Dummy NPC does not exist|r");
                return;
            }

            var npcSpawner = new NpcSpawner
            {
                Id = 0,
                UnitId = DUMMY_NPC_TEMPLATE_ID,
                Position = character.Position.Clone()
            };
            
            (newX, newY) = MathUtil.AddDistanceToFront(3f, character.Position.X, character.Position.Y, character.Position.RotationZ);
            npcSpawner.Position.Y = newY;
            npcSpawner.Position.X = newX;
            angle = MathUtil.CalculateAngleFrom(npcSpawner.Position.X, npcSpawner.Position.Y, character.Position.X, character.Position.Y);
            newRotZ = MathUtil.ConvertDegreeToDirection(angle);
            npcSpawner.Position.RotationX = 0;
            npcSpawner.Position.RotationY = 0;
            npcSpawner.Position.RotationZ = newRotZ;
            npcSpawner.SpawnAll();
        }
    }
}
