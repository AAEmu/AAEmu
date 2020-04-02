using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using NLog;
using System;

namespace AAEmu.Game.Scripts.Commands
{
    public class SpawnGrid : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public void OnLoad()
        {
            string[] names = { "spawngrid", "spawngroup" };
            CommandManager.Instance.Register(names, this);
        }

        public string GetCommandLineHelp()
        {
            return "<npc||doodad> <templateID> <columns> <rows> <spacing>";
        }

        public string GetCommandHelpText()
        {
            return "Spawns a large amount of NPCs or doodads using <templateID> as a template in a grid in front of you using specified number of colums, rows and spacing.\n" +
                "Example: " + CommandManager.CommandPrefix + "spawngrid doodad 320 5 5 2";
        }

        public void SpawnDoodad(uint unitId, Character character, float newX, float newY)
        {
            var doodadSpawner = new DoodadSpawner();
            doodadSpawner.Id = 0;
            doodadSpawner.UnitId = unitId;
            doodadSpawner.Position = character.Position.Clone();
            doodadSpawner.Position.Y = newY;
            doodadSpawner.Position.X = newX;
            double angle = MathUtil.CalculateAngleFrom(doodadSpawner.Position.Y, doodadSpawner.Position.X, character.Position.Y, character.Position.X);
            sbyte newRotZ = MathUtil.ConvertDegreeToDoodadDirection(angle);
            doodadSpawner.Position.RotationX = 0;
            doodadSpawner.Position.RotationY = 0;
            doodadSpawner.Position.RotationZ = newRotZ;
            doodadSpawner.Spawn(0);
        }

        public void SpawnNPC(uint unitId,Character character,float newX, float newY)
        {
            var npcSpawner = new NpcSpawner();
            npcSpawner.Id = 0;
            npcSpawner.UnitId = unitId;
            npcSpawner.Position = character.Position.Clone();
            npcSpawner.Position.Y = newY;
            npcSpawner.Position.X = newX;
            var angle = MathUtil.CalculateAngleFrom(npcSpawner.Position.X, npcSpawner.Position.Y, character.Position.X, character.Position.Y);
            sbyte newRotZ = MathUtil.ConvertDegreeToDirection(angle);
            npcSpawner.Position.RotationX = 0;
            npcSpawner.Position.RotationY = 0;
            npcSpawner.Position.RotationZ = newRotZ;
            npcSpawner.SpawnAll();
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 5)
            {
                character.SendMessage("[Spawn] " + CommandManager.CommandPrefix + "spawngrid " + GetCommandLineHelp());
                return;
            }

            string action = args[0].ToLower();
            uint templateId ;
            uint columns;
            uint rows;
            float spacing;
            if (!uint.TryParse(args[1],out templateId) || !uint.TryParse(args[2], out columns) || !uint.TryParse(args[3], out rows) || !float.TryParse(args[4], out spacing))
            {
                character.SendMessage("|cFFFF0000[Spawn] Parse error|r");
                return;
            }
            if (columns < 1) 
                columns = 1;
            if (rows < 1)
                rows = 1;
            if (spacing < 0.1f)
                spacing = 0.1f;

            switch(action)
            {
                case "npc":
                    if (!NpcManager.Instance.Exist(templateId))
                    {
                        character.SendMessage("|cFFFF0000[Spawn] NPC {0} don't exist|r", templateId);
                        return;
                    }
                    break;
                case "doodad":
                    if (!DoodadManager.Instance.Exist(templateId))
                    {
                        character.SendMessage("|cFFFF0000[Spawn] Doodad {0} don't exist|r", templateId);
                        return;
                    }
                    break;
                default:
                    character.SendMessage("|cFFFF0000[Spawn] Unknown object type.|r");
                    return;
            }

            float newX;
            float newY;
            float startX;
            float startY;

            // Origin point for spawns
            (startX, startY) = MathUtil.AddDistanceToFront(3f, character.Position.X, character.Position.Y, character.Position.RotationZ);
            for (var y = 0; y < rows; y++)
            {
                float sizeY = rows * spacing;
                float posY = y * spacing;
                for (var x = 0; x < columns; x++)
                {
                    float sizeX = columns * spacing;
                    float posX = (x * spacing) - (sizeX / 2);
                    (newX, newY) = MathUtil.AddDistanceToFront(posY, startX, startY, character.Position.RotationZ);
                    (newX, newY) = MathUtil.AddDistanceToRight(posX, newX, newY, character.Position.RotationZ);
                    switch(action)
                    {
                        case "npc":
                            SpawnNPC(templateId, character, newX, newY);
                            break;
                        case "doodad":
                            SpawnDoodad(templateId, character, newX, newY);
                            break;
                    }
                }
            }

        }
    }
}
