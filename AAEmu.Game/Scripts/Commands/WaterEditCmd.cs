using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class WaterEditCmd : SubCommandBase, ICommand, ICommandV2
    {
        public static WaterBodyArea SelectedWater { get; set; }
        public static World SelectedWorld { get; set; }
        public static int NextPoint { get; set; }
        public static List<(WaterBodyArea, float)> NearbyList = new List<(WaterBodyArea, float)>();
        public static List<BaseUnit> Markers = new List<BaseUnit>();
        public static WaterEditRecordTask RecordingTask { get; set; }
        public static List<Vector3> RecordedData { get; set; } = new List<Vector3>();
        public static Character RecordingCharacter { get; set; }
        
        public WaterEditCmd()
        {
            Title = "[WaterEdit]";
            Description = "Root command for live editing, saving and loading of water bodies";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit";

            Register(new WaterEditListSubCommand(), "list", "l");
            Register(new WaterEditNearbySubCommand(), "nearby");
            Register(new WaterEditLoadSubCommand(), "load");
            Register(new WaterEditSaveSubCommand(), "save");
            Register(new WaterEditSelectSubCommand(), "select", "s");
            Register(new WaterEditClearSubCommand(), "clear", "c");
            Register(new WaterEditGoToSubCommand(), "goto", "g");
            Register(new WaterEditNextSubCommand(), "next", "n");
            Register(new WaterEditSetBottomSubCommand(), "setbottom", "sb");
            Register(new WaterEditSetHeightSubCommand(), "setheight", "sh");
            Register(new WaterEditListPointsSubCommand(), "listpoints", "lp");
            Register(new WaterEditMovePointSubCommand(), "movepoint", "mp");
            Register(new WaterEditInsertPointSubCommand(), "insertpoint", "ip");
            Register(new WaterEditRemovePointSubCommand(), "removepoint", "rp", "dp");
            Register(new WaterEditRemoveWaterSubCommand(), "removewater", "rw");
            Register(new WaterEditRecordCurrentSubCommand(), "recordcurrent", "rec");
            Register(new WaterEditCreateFromRecordingSubCommand(), "createfromrecording");
        }
        
        public void OnLoad()
        {
            string[] name = { "wateredit", "water_edit", "wedit"};
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return $"<{string.Join("||", SupportedCommands)}>";
        }

        public string GetCommandHelpText()
        {
            return CallPrefix;
        }

        public void Execute(Character character, string[] args)
        {
            throw new InvalidOperationException($"A {nameof(ICommandV2)} implementation should not be used as ICommand interface");
        }
        
        public static void CreateNearbyList(ICharacter character, World world)
        {
            NearbyList.Clear();
            lock (world.Water._lock)
            {
                foreach (var area in world.Water.Areas)
                {
                    var offsetVec = area.Points[0] - character.Transform.World.Position;
                    var dist = offsetVec.Length();

                    if (NearbyList.Count <= 0)
                    {
                        NearbyList.Add((area, dist));
                    }
                    else
                    {
                        var insertAt = NearbyList.Count;
                        for (var i = 0; i < NearbyList.Count; i++)
                        {
                            if (NearbyList[i].Item2 > dist)
                            {
                                insertAt = i;
                                break;
                            }
                        }

                        NearbyList.Insert(insertAt, (area, dist));
                    }
                }
            }
        }
        
        public static void ShowSelectedArea(ICharacter character)
        {
            var bottomDoodadId = 4763u; // Crescent Throne Flag 
            var centerSurfaceDoodadId = 5014u; // Combat Flag
            var middleDoodadId = 5622u; // Stone Post
            var topNpcId = 13013u; // Forward Scarecrow
            foreach (var marker in Markers)
            {
                ObjectIdManager.Instance.ReleaseId(marker.ObjId);
                marker.Delete();
            }

            Markers.Clear();
            
            if ((SelectedWorld == null) || (SelectedWater == null))
                return;

            lock (SelectedWorld.Water._lock)
            {
                SelectedWater.UpdateBounds();

                var centerDoodad = DoodadManager.Instance.Create(0, centerSurfaceDoodadId);
                centerDoodad.Transform.Local.SetPosition(SelectedWater.GetCenter(true));
                centerDoodad.Show();
                Markers.Add(centerDoodad);
                // character.SendMessage("--" + centerDoodad.Transform.ToFullString());

                for (var p = 0; p < SelectedWater.Points.Count - 1; p++)
                {
                    var point = SelectedWater.Points[p];
                    var bottomDoodad = DoodadManager.Instance.Create(0, bottomDoodadId);
                    bottomDoodad.Transform.Local.SetPosition(point);
                    bottomDoodad.Show();
                    Markers.Add(bottomDoodad);

                    var surfaceUnit = NpcManager.Instance.Create(0, topNpcId);
                    surfaceUnit.Transform.Local.SetPosition(point);
                    surfaceUnit.Transform.Local.SetHeight(point.Z + SelectedWater.Height);
                    if ((p != 0) || (SelectedWater.AreaType == WaterBodyAreaType.LineArray))
                        surfaceUnit.Name = "#" + p.ToString();
                    else
                        surfaceUnit.Name = "#" + p.ToString() + " <-> #" + (SelectedWater.Points.Count - 1).ToString();
                    surfaceUnit.Faction = FactionManager.Instance.GetFaction(FactionsEnum.Friendly);
                    surfaceUnit.Show();
                    Markers.Add(surfaceUnit);

                    var dividers = MathF.Ceiling(SelectedWater.Height / 5f);
                    dividers = MathF.Max(dividers, 2f);

                    for (int i = 1; i < dividers; i++)
                    {
                        var h = SelectedWater.Height / dividers * (float)i;
                        var middleDoodad = DoodadManager.Instance.Create(0, middleDoodadId);
                        middleDoodad.Transform.Local.SetPosition(point);
                        middleDoodad.Transform.Local.SetHeight(point.Z + h);
                        middleDoodad.Show();
                        Markers.Add(middleDoodad);
                    }
                }
            }
        }

        public static void ShowRecordedArea(ICharacter character)
        {
            var bottomDoodadId = 4763u; // Crescent Throne Flag 
            var centerSurfaceDoodadId = 5014u; // Combat Flag
            var middleDoodadId = 5622u; // Stone Post
            var topNpcId = 13013u; // Forward Scarecrow
            foreach (var marker in Markers)
            {
                ObjectIdManager.Instance.ReleaseId(marker.ObjId);
                marker.Delete();
            }

            Markers.Clear();

            var totalDistance = 0f;

            for (var p = 0; p < RecordedData.Count - 1; p++)
            {
                var point = RecordedData[p];
                if (p > 0)
                    totalDistance += (point - RecordedData[p - 1]).Length();

                /*
                var bottomDoodad = DoodadManager.Instance.Create(0, bottomDoodadId);
                bottomDoodad.Transform.Local.SetPosition(point);
                bottomDoodad.Show();
                Markers.Add(bottomDoodad);
                */

                var surfaceUnit = NpcManager.Instance.Create(0, topNpcId);
                surfaceUnit.Transform.Local.SetPosition(point);
                surfaceUnit.Name = "#" + p.ToString();
                surfaceUnit.Faction = FactionManager.Instance.GetFaction(FactionsEnum.Friendly);
                surfaceUnit.Show();
                Markers.Add(surfaceUnit);

                /*
                var dividers = MathF.Ceiling(SelectedWater.Height / 5f);
                dividers = MathF.Max(dividers, 2f);

                for (int i = 1; i < dividers; i++)
                {
                    var h = SelectedWater.Height / dividers * (float)i;
                    var middleDoodad = DoodadManager.Instance.Create(0, middleDoodadId);
                    middleDoodad.Transform.Local.SetPosition(point);
                    middleDoodad.Transform.Local.SetHeight(point.Z + h);
                    middleDoodad.Show();
                    Markers.Add(middleDoodad);
                }
                */
            }

            var averageSpeed = totalDistance / (RecordedData.Count - 1);
            character?.SendMessage($"[WaterEdit] Recoded data: {RecordedData.Count} points, average speed: {averageSpeed} m/s (over {totalDistance} m)");
        }

        public static void OnRecodingEnded()
        {
            // Do stuff
            RecordingCharacter?.SendMessage($"[WaterEdit] Recoding ended! Recorded {RecordedData.Count} points.");
            ShowRecordedArea(RecordingCharacter);
        }
    }
}
