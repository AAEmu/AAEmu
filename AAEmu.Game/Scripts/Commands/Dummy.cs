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
            float angle;
            
            if (!NpcManager.Instance.Exist(DUMMY_NPC_TEMPLATE_ID))
            {
                character.SendMessage("|cFFFF0000[Dummy] Dummy NPC does not exist|r");
                return;
            }

            var spawnPos = character.Transform.CloneDetached();
            spawnPos.Local.AddDistanceToFront(3f);

            var npcSpawner = new NpcSpawner
            {
                Id = 0,
                UnitId = DUMMY_NPC_TEMPLATE_ID,
                Position = character.Transform.CloneAsSpawnPosition()
            };
            angle = (float)MathUtil.CalculateAngleFrom(npcSpawner.Position.X, npcSpawner.Position.Y, character.Transform.World.Position.X, character.Transform.World.Position.Y);
            npcSpawner.Position.Yaw = 0;
            npcSpawner.Position.Pitch = 0;
            npcSpawner.Position.Roll = angle.DegToRad();
            npcSpawner.SpawnAll();
        }
    }
}
