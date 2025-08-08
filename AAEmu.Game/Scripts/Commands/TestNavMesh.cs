using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestNavMesh : ICommand
{
    public string[] CommandNames { get; set; } = ["testnavmesh", "test_navmesh"];
    public static List<BaseUnit> Markers { get; set; } = [];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Shows route to target";
    }

    private static void ClearMarkers()
    {
        foreach (var marker in Markers)
        {
            ObjectIdManager.Instance.ReleaseId(marker.ObjId);
            marker.Delete();
        }
        Markers.Clear();
    }

    private static void AddDoodadMarker(WorldInstance world, Vector3 pos, uint doodadTemplateId)
    {
        var markerDoodad = DoodadManager.Instance.Create(world, 0, doodadTemplateId);
        markerDoodad.Transform.Local.SetPosition(pos);
        markerDoodad.Show();
        Markers.Add(markerDoodad);
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var stonePostDoodad = 5622u; // Stone Post
        var crescentThroneFlagDoodad = 4763u; // Crescent Throne Flag 
        
        if (character.CurrentTarget is not Npc npc)
        {
            messageOutput.SendMessage("You need to have a target selected.");
            return;
        }
        ClearMarkers();
        var world = character.ParentWorld;
        var pos = world.Template.GeoData.FindСlosestToTheCurrent(npc.Transform.ZoneId, npc.Transform.World.Position);
        messageOutput.SendMessage($"Closest to {npc.Transform.World.Position} -> {pos}");
        var foundPath = npc.Ai.PathNode.FindPath(npc.ParentWorld, npc.Transform.World.Position, character.Transform.World.Position).ToList();
        foundPath.Insert(0, npc.Transform.World.Position);
        foundPath.Add(character.Transform.World.Position);
        //npc.Ai.PathNode.FoundPath = foundPath;
        foreach (var v3 in foundPath)
        {
            messageOutput.SendMessage($"-> {v3}");
            AddDoodadMarker(world, v3, stonePostDoodad);
        }
        messageOutput.SendMessage($"Reduced:");
        var reducedPath = world.Template.GeoData.ReducePath(foundPath.ToList(), 10).ToList();
        //reducedPath.Insert(0, npc.Transform.World.Position);
        //reducedPath.Add(character.Transform.World.Position);
        foreach (var v3 in reducedPath)
        {
            messageOutput.SendMessage($"=> {v3}");
            AddDoodadMarker(world, v3, crescentThroneFlagDoodad);
        }
    }
}
