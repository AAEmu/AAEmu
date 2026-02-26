using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

using NLog;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Imports a navmesh binary file into the current world instance, replacing the built navmesh.
/// Usage: /importnavmesh &lt;path_to_navmesh.bin&gt;
///
/// The file must be in DotRecast/Recast Navigation binary format (.bin),
/// as exported by /exportnavmesh or built by the Recast demo tool.
/// </summary>
public class ImportNavMeshCommand : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public string[] CommandNames { get; set; } = ["importnavmesh", "inm"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "<path_to_navmesh.bin>";

    public string GetCommandHelpText() =>
        "Imports a DotRecast/Recast Navigation navmesh binary file (.bin) into the current world, " +
        "replacing the runtime-built navmesh. Use /exportnavmesh to create the file first.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 1)
        {
            Log(messageOutput, "Usage: /importnavmesh <path_to_navmesh.bin>");
            return;
        }

        var filePath = string.Join(" ", args);

        if (!File.Exists(filePath))
        {
            Log(messageOutput, $"File not found: {filePath}");
            return;
        }

        var world = character.ParentWorld;
        if (world == null)
        {
            Log(messageOutput, "No world instance found.");
            return;
        }

        if (world.NavMesh == null)
        {
            Log(messageOutput, "No NavMeshManager on current world instance.");
            return;
        }

        Log(messageOutput, $"Importing navmesh from: {filePath}");

        try
        {
            if (world.NavMesh.ImportNavMesh(filePath))
            {
                var tileCount = world.NavMesh.TileCount;
                Log(messageOutput, $"Successfully imported navmesh: {tileCount} tiles loaded.");
                Log(messageOutput, "NPCs will use the imported navmesh for height/pathfinding.");
            }
            else
            {
                Log(messageOutput, "Failed to import navmesh — file may be corrupt or incompatible.");
            }
        }
        catch (Exception ex)
        {
            Log(messageOutput, $"Error importing navmesh: {ex.Message}");
            Logger.Error(ex, "[ImportNavMesh] Exception during import");
        }
    }

    private void Log(IMessageOutput messageOutput, string text)
    {
        CommandManager.SendNormalText(this, messageOutput, text);
        Logger.Info("[ImportNavMesh] " + text);
    }
}
