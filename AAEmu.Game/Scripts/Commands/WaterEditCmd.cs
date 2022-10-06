using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using System.Collections.Generic;
using System.Drawing;
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
        public static float RecordedSpeed { get; set; }
        public static Character RecordingCharacter { get; set; }
        
        public WaterEditCmd()
        {
            Title = "[WaterEdit]";
            Description = "Root command for live editing, saving and loading of water bodies";
            CallPrefix = $"{CommandManager.CommandPrefix}wateredit";

            Register(new WaterEditListSubCommand(), "list", "l");
            Register(new WaterEditNearbySubCommand(), "nearby", "around", "a");
            Register(new WaterEditLoadSubCommand(), "load");
            Register(new WaterEditSaveSubCommand(), "save");
            Register(new WaterEditSelectSubCommand(), "select", "s");
            Register(new WaterEditClearSubCommand(), "clear", "c");
            Register(new WaterEditGoToSubCommand(), "goto", "g");
            Register(new WaterEditNextSubCommand(), "next", "n");
            Register(new WaterEditSetZSubCommand(), "setz", "sz");
            Register(new WaterEditSetDepthSubCommand(), "setdepth", "sd");
            Register(new WaterEditSetWidthSubCommand(), "setwidth", "sw");
            Register(new WaterEditSetSpeedSubCommand(), "setspeed", "speed", "ss");
            Register(new WaterEditListPointsSubCommand(), "listpoints", "lp");
            Register(new WaterEditMovePointSubCommand(), "movepoint", "mp");
            Register(new WaterEditInsertPointSubCommand(), "insertpoint", "ip");
            Register(new WaterEditRemovePointSubCommand(), "removepoint", "rp", "dp");
            Register(new WaterEditRemoveWaterSubCommand(), "removewater", "rw");
            Register(new WaterEditRecordCurrentSubCommand(), "recordcurrent", "rec");
            Register(new WaterEditCreateWaterSubCommand(), "createwater");
            Register(new WaterEditCreateFromRecordingSubCommand(), "createfromrecording");
            Register(new WaterEditDowngradeSubCommand(), "downgrade");
            Register(new WaterEditOffsetSubCommand(), "offset");
            Register(new WaterEditShowSurfaceSubCommand(), "showsurface");
            Register(new WaterEditSplitRiverSubCommand(), "splitriver", "split");
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
        
        public static void ShowSelectedArea(ICharacter character, bool clearOldMarkers = true, bool useVirtualBorder = false)
        {
            var bottomDoodadId = 4763u; // Crescent Throne Flag 
            var centerSurfaceDoodadId = 5014u; // Combat Flag
            var middleDoodadId = 5622u; // Stone Post
            var topNpcId = 13013u; // Forward Scarecrow

            if (clearOldMarkers)
            {
                foreach (var marker in Markers)
                {
                    ObjectIdManager.Instance.ReleaseId(marker.ObjId);
                    marker.Delete();
                }
                Markers.Clear();
            }

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

                var ShowPoints = useVirtualBorder ? SelectedWater.BorderPoints : SelectedWater.Points;

                for (var p = 0; p < ShowPoints.Count - 1; p++)
                {
                    var point = ShowPoints[p];
                    var directionVector = Vector3.Zero;

                    var bottomDoodad = DoodadManager.Instance.Create(0, bottomDoodadId);
                    bottomDoodad.Transform.Local.SetPosition(point);
                    bottomDoodad.Transform.Local.SetHeight(point.Z - SelectedWater.Depth);
                    bottomDoodad.Show();
                    Markers.Add(bottomDoodad);

                    var surfaceUnit = NpcManager.Instance.Create(0, topNpcId);
                    surfaceUnit.Transform.Local.SetPosition(point);
                    
                    if ((p == 0) && (SelectedWater.AreaType == WaterBodyAreaType.Polygon) && (useVirtualBorder == false))
                        surfaceUnit.Name = "#" + p.ToString() + " <-> #" + (ShowPoints.Count - 1).ToString();
                    else
                        surfaceUnit.Name = "#" + p.ToString();
                    
                    surfaceUnit.Faction = FactionManager.Instance.GetFaction(FactionsEnum.Friendly);
                    surfaceUnit.Show();
                    Markers.Add(surfaceUnit);
                    
                    if (SelectedWater.AreaType == WaterBodyAreaType.LineArray)
                    {
                        if (p >= ShowPoints.Count - 1)
                            directionVector = Vector3.Normalize(ShowPoints[^1] - ShowPoints[^2]);
                        else
                            directionVector = Vector3.Normalize(ShowPoints[p + 1] - ShowPoints[p]);
                    }

                    var dividers = MathF.Ceiling(SelectedWater.Depth / 5f);
                    dividers = MathF.Max(dividers, 2f);

                    for (int i = 1; i < dividers; i++)
                    {
                        var h = SelectedWater.Depth / dividers * (float)i;
                        var middleDoodad = DoodadManager.Instance.Create(0, middleDoodadId);
                        middleDoodad.Transform.Local.SetPosition(point);
                        middleDoodad.Transform.Local.SetHeight(point.Z - SelectedWater.Depth + h);
                        middleDoodad.Show();
                        Markers.Add(middleDoodad);

                        // If a line, also add markers on the width border
                        if ((SelectedWater.AreaType == WaterBodyAreaType.LineArray) && (useVirtualBorder == false))
                        {
                            var perpendicular = Vector3.Normalize(Vector3.Cross(directionVector, Vector3.UnitZ));
                            var rightDoodad = DoodadManager.Instance.Create(0, middleDoodadId);
                            rightDoodad.Transform = middleDoodad.Transform.CloneDetached();
                            rightDoodad.Transform.Local.AddDistance(perpendicular * SelectedWater.RiverWidth);
                            rightDoodad.Show();
                            Markers.Add(rightDoodad);
                            
                            var leftDoodad = DoodadManager.Instance.Create(0, middleDoodadId);
                            leftDoodad.Transform = middleDoodad.Transform.CloneDetached();
                            leftDoodad.Transform.Local.AddDistance(perpendicular * SelectedWater.RiverWidth * -1f);
                            leftDoodad.Show();
                            Markers.Add(leftDoodad);
                        }

                    }
                    
                }
            }
        }

        public static void ShowRecordedArea(ICharacter character)
        {
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

                var surfaceUnit = NpcManager.Instance.Create(0, topNpcId);
                surfaceUnit.Transform.Local.SetPosition(point);
                surfaceUnit.Name = "#" + p.ToString();
                surfaceUnit.Faction = FactionManager.Instance.GetFaction(FactionsEnum.Friendly);
                surfaceUnit.Show();
                Markers.Add(surfaceUnit);
            }

            var averageSpeed = totalDistance / (RecordedData.Count - 1);
            RecordedSpeed = MathF.Round(averageSpeed, 1);
            character?.SendMessage($"[WaterEdit] Recoded data: {RecordedData.Count} points, average speed: |cFFFFFF00{averageSpeed} m/s|r over {totalDistance:F2} m (|cFF00FF00{RecordedSpeed}|r)");
        }

        public static void ShowSelectedSurface(ICharacter character)
        {
            var surfaceDoodadId = 5622u; // Stone Post
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

                RectangleF bb = SelectedWater.BoundingBox;
                bb.Inflate(1f, 1f);
                var rr = new Rectangle((int)MathF.Round(bb.Left), (int)MathF.Round(bb.Top),
                    (int)MathF.Round(bb.Width), (int)MathF.Round(bb.Height));
                
                for(var y = rr.Top; y <= rr.Bottom; y += 2)
                for (int x = rr.Left; x <= rr.Right; x += 2)
                {
                    var p = new Vector3(x, y, SelectedWater.Highest);
                    if (SelectedWater.GetSurface(p, out var surfacePoint, out _))
                    {
                        var markerDoodad = DoodadManager.Instance.Create(0, surfaceDoodadId);
                        markerDoodad.Transform.Local.SetPosition(surfacePoint);
                        markerDoodad.Show();
                        Markers.Add(markerDoodad);
                    }
                }
                
            }

            ShowSelectedArea(character, false, true);
        }
        
        public static void OnRecodingEnded()
        {
            // Do stuff
            RecordingCharacter?.SendMessage($"[WaterEdit] Recoding ended! Recorded {RecordedData.Count} points.");
            ShowRecordedArea(RecordingCharacter);
        }
    }
}
