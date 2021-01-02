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
using System.Globalization;

namespace AAEmu.Game.Scripts.Commands
{
    public class Spawn : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            CommandManager.Instance.Register("spawn", this);
        }

        public string GetCommandLineHelp()
        {
            return "<npc||doodad||remove> <unitId> [rotationZ]";
        }

        public string GetCommandHelpText()
        {
            return "Spawns a npc or doodad using <unitId> as a template. Or remove a doodad";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 2)
            {
                character.SendMessage("[Spawn] " + CommandManager.CommandPrefix + "spawn <npc||doodad||remove> <unitId> [rotationZ]");
                return;
            }

            if (uint.TryParse(args[1], out var unitId))
            {
                // character.SendMessage("[Spawn] Arg 0 --- {0} Arg 1 {1}", args[0], args[1]);

                float newX;
                float newY;
                float angle;
                float newRotZ;
                switch (args[0])
                {
                    case "remove":
                        var myDoodad = WorldManager.Instance.GetDoodad(unitId);

                        if ((myDoodad != null) && (myDoodad is Doodad))
                        {
                            character.SendMessage("[Spawn] Removing Doodad with ID {0}", myDoodad.ObjId);
                            ObjectIdManager.Instance.ReleaseId(myDoodad.ObjId);
                            myDoodad.Delete();
                        }
                        else
                        {
                            character.SendMessage("|cFFFF0000[Spawn] Doodad with Id {0} Does not exist |r", unitId);
                        }
                        break;
                    case "npc":
                        if (!NpcManager.Instance.Exist(unitId))
                        {
                            character.SendMessage("|cFFFF0000[Spawn] NPC {0} don't exist|r", unitId);
                            return;
                        }
                        var npcSpawner = new NpcSpawner();
                        npcSpawner.Id = 0;
                        npcSpawner.UnitId = unitId;
                        npcSpawner.Position = character.Transform.CloneAsSpawnPosition();
                        (newX, newY) = MathUtil.AddDistanceToFront(3f, character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.ToYawPitchRoll().Z);
                        npcSpawner.Position.Y = newY;
                        npcSpawner.Position.X = newX;
                        angle = (float)MathUtil.CalculateAngleFrom(npcSpawner.Position.X, npcSpawner.Position.Y, character.Transform.World.Position.X, character.Transform.World.Position.Y);
                        if ((args.Length > 2) && (float.TryParse(args[2], NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out newRotZ)))
                        {
                            angle = (float)MathUtil.DegreeToRadian(newRotZ);
                            character.SendMessage("[Spawn] NPC {0} using angle {1}° = {2} rad", unitId, newRotZ, angle);
                        }
                        npcSpawner.Position.Yaw = 0;
                        npcSpawner.Position.Pitch = 0;
                        npcSpawner.Position.Roll = angle;
                        npcSpawner.SpawnAll();
                        character.SendMessage("[Spawn] NPC {0} spawned with sbyte-angle {1}", unitId, angle);
                        break;
                    case "doodad":
                        if (!DoodadManager.Instance.Exist(unitId))
                        {
                            character.SendMessage("|cFFFF0000[Spawn] Doodad {0} don't exist|r", unitId);
                            return;
                        }
                        var doodadSpawner = new DoodadSpawner();
                        doodadSpawner.Id = 0;
                        doodadSpawner.UnitId = unitId;
                        doodadSpawner.Position = character.Transform.CloneAsSpawnPosition();
                        (newX, newY) = MathUtil.AddDistanceToFront(3f, character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.ToYawPitchRoll().Z);
                        doodadSpawner.Position.Y = newY;
                        doodadSpawner.Position.X = newX;
                        angle = (float)MathUtil.CalculateAngleFrom(doodadSpawner.Position.Y, doodadSpawner.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.X);
                        if ((args.Length > 2) && (float.TryParse(args[2], NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var degrees)))
                        {
                            angle = (float)MathUtil.DegreeToRadian(degrees);
                            character.SendMessage("[Spawn] Doodad {0} using user provided angle {1}° = {2} rad", unitId, degrees, angle);
                        }
                        else
                            character.SendMessage("[Spawn] Doodad {0} facing you, using characters angle {1}°", unitId, angle);
                        doodadSpawner.Position.Yaw = 0;
                        doodadSpawner.Position.Pitch = 0;
                        doodadSpawner.Position.Roll = angle;
                        doodadSpawner.Spawn(0, 0, character.ObjId);
                        break;
                }
            }
            else
                character.SendMessage("|cFFFF0000[Spawn] Throw parse unitId|r");
        }
    }
}
