using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Height : ICommand
{
    public string[] CommandNames { get; set; } = ["height"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target)";
    }

    public string GetCommandHelpText()
    {
        return "Gets your or target's Z, FloorQuery result, terrain Blerp, and nearest nav node height";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var targetPlayer = character;
        if (args.Length > 0)
        {
            targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstArg);
        }

        var pos = targetPlayer.Transform.World.Position;
        var floorHit = targetPlayer.ParentWorld.Template.Floor.QueryFloor(pos.X, pos.Y, pos.Z, FloorContext.Debug);
        var terrainZ = targetPlayer.ParentWorld.Template.GetHeight(pos.X, pos.Y);
        var navNodeZ = targetPlayer.ParentWorld.Template.GeoData.GetHeight(pos);

        var mode = AppConfiguration.Instance.World.FloorSource;
        CommandManager.SendNormalText(
            this,
            messageOutput,
            $"{targetPlayer.Name} Z={pos.Z:0.###} Floor={floorHit.Z:0.###} mode={mode} src={floorHit.Provider} Terrain={terrainZ:0.###} Nav={navNodeZ:0.###} deltaNav={floorHit.DeltaNav:0.###} deltaTerrain={floorHit.DeltaTerrain:0.###}");
    }
}
