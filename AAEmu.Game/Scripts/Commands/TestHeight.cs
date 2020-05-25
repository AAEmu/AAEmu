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
using AAEmu.Game.Models.Game.Units.Movements;


namespace AAEmu.Game.Scripts.Commands
{
    public class TestHeight : ICommand
    {
        public void OnLoad()
        {
            string[] names = { "testheightvisualizer", "test_height_visualizer" };
            CommandManager.Instance.Register(names, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) [testpos||mark||line]";
        }

        public string GetCommandHelpText()
        {
            return "Gets your or target's current height and that of the supposed floor (using heightmap data)\n" +
                "testpos will move you near freedich underwater\r" +
                "mark creates a grid of pillar doodads used for measuring the floor at 2m intervals (exact heightmap points)\r" +
                "line creates a cross of pillar doodads used for measuring the floor at 1m intervals (for in-between points)";
        }

        public void Execute(Character character, string[] args)
        {
            Character targetPlayer = character;
            var firstarg = 0;
            if (args.Length > 0)
                targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out firstarg);

            if ((args.Length > firstarg) && (args[firstarg] == "testpos"))
            {
                targetPlayer.DisabledSetPosition = true;
                targetPlayer.SendPacket(new SCTeleportUnitPacket(0, 0, 22500f, 18500f, 10f, 0f));
                targetPlayer.SendMessage("[Move] |cFFFFFFFF{0}|r moved to X: {1}, Y: {2}, Z: {3}", targetPlayer.Name, 22500f, 18500f, 10f);
            }
            else
            if ((args.Length > firstarg) && (args[firstarg] == "mark"))
            { 
                // Place markers
                var rX = (int)Math.Floor(targetPlayer.Position.X);
                rX = rX - (rX % 2);
                var rY = (int)Math.Floor(targetPlayer.Position.Y);
                rY = rY - (rY % 2);
                uint unitId = 5622;
                for (var y = rY - 10; y <= rY + 10; y += 2)
                    for (var x = rX - 10; x <= rX + 10; x += 2)
                    {
                        if (!DoodadManager.Instance.Exist(unitId))
                        {
                            return;
                        }
                        var doodadSpawner = new DoodadSpawner();
                        doodadSpawner.Id = 0;
                        doodadSpawner.UnitId = unitId;
                        doodadSpawner.Position = character.Position.Clone();
                        doodadSpawner.Position.X = x;
                        doodadSpawner.Position.Y = y;
                        doodadSpawner.Position.Z = WorldManager.Instance.GetWorldByZone(targetPlayer.Position.ZoneId).GetHeight(x, y);
                        doodadSpawner.Position.RotationX = 0;
                        doodadSpawner.Position.RotationY = 0;
                        doodadSpawner.Position.RotationZ = 0;
                        doodadSpawner.Spawn(0);
                    }

            }
            else
            if ((args.Length > firstarg) && (args[firstarg] == "line"))
            {
                // Place markers
                var rXX = (int)Math.Floor(targetPlayer.Position.X);
                rXX = rXX - (rXX % 2);
                var rYY = (int)Math.Floor(targetPlayer.Position.Y);
                rYY = rYY - (rYY % 2);

                float rX = rXX;
                float rY = rYY;
                uint unitId = 5622;
                for (var x = rX - 10f; x <= rX + 10f; x += 1f)
                {
                    if (!DoodadManager.Instance.Exist(unitId))
                    {
                        return;
                    }
                    var doodadSpawner = new DoodadSpawner();
                    doodadSpawner.Id = 0;
                    doodadSpawner.UnitId = unitId;
                    doodadSpawner.Position = character.Position.Clone();
                    doodadSpawner.Position.X = x;
                    doodadSpawner.Position.Y = rY;
                    doodadSpawner.Position.Z = WorldManager.Instance.GetWorldByZone(targetPlayer.Position.ZoneId).GetHeight(x, rY);
                    doodadSpawner.Position.RotationX = 0;
                    doodadSpawner.Position.RotationY = 0;
                    doodadSpawner.Position.RotationZ = 0;
                    doodadSpawner.Spawn(0);
                }
                for (var y = rY - 10f; y <= rY + 10f; y += 1f)
                {
                    if (!DoodadManager.Instance.Exist(unitId))
                    {
                        return;
                    }
                    var doodadSpawner = new DoodadSpawner();
                    doodadSpawner.Id = 0;
                    doodadSpawner.UnitId = unitId;
                    doodadSpawner.Position = character.Position.Clone();
                    doodadSpawner.Position.X = rX;
                    doodadSpawner.Position.Y = y;
                    doodadSpawner.Position.Z = WorldManager.Instance.GetWorldByZone(targetPlayer.Position.ZoneId).GetHeight(rX, y);
                    doodadSpawner.Position.RotationX = 0;
                    doodadSpawner.Position.RotationY = 0;
                    doodadSpawner.Position.RotationZ = 0;
                    doodadSpawner.Spawn(0);
                }

            }
            else
            {
                // Show info
                var world = WorldManager.Instance.GetWorldByZone(targetPlayer.Position.ZoneId);

                var height = world.GetHeight(targetPlayer.Position.X, targetPlayer.Position.Y);
                var hDelta = character.Position.Z - height;
                character.SendMessage("[Height] {2} Z-Pos: {0} - Floor: {1} - HeightmapDelta: {3}", character.Position.Z, height, targetPlayer.Name, hDelta);

                character.SendMessage("[Position] |cFFFFFFFF{0}|r X: |cFFFFFFFF{1:F1}|r  Y: |cFFFFFFFF{2:F1}|r  Z: |cFFFFFFFF{3:F1}|r ",
                    targetPlayer.Name, targetPlayer.Position.X, targetPlayer.Position.Y, targetPlayer.Position.Z);

                var borderLeft = (int)Math.Floor(targetPlayer.Position.X);
                borderLeft = borderLeft - (borderLeft % 2);
                var borderRight = borderLeft + 2; // we're using a divider of 2 of the heightmaps in memory, so we need to compensate with that in mind (instead of 1)
                var borderBottom = (int)Math.Floor(targetPlayer.Position.Y);
                borderBottom = borderBottom - (borderBottom % 2);
                var borderTop = borderBottom + 2;

                // Get heights for these points
                var heightTL = world.GetRawHeightMapHeight(borderLeft, borderTop);        // 10
                var heightTR = world.GetRawHeightMapHeight(borderRight, borderTop);       // 6
                var heightBL = world.GetRawHeightMapHeight(borderLeft, borderBottom);     // 14
                var heightBR = world.GetRawHeightMapHeight(borderRight, borderBottom);    // 16
                character.SendMessage("[Height] TL @ {0}x{1} = {2}", borderLeft, borderTop, heightTL);
                character.SendMessage("[Height] TR @ {0}x{1} = {2}", borderRight, borderTop, heightTR);
                character.SendMessage("[Height] BL @ {0}x{1} = {2}", borderLeft, borderBottom, heightBL);
                character.SendMessage("[Height] BR @ {0}x{1} = {2}", borderRight, borderBottom, heightBR);
            }
        }
    }
}
