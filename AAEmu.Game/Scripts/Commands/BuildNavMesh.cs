using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

using NLog;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Forces navmesh build for all loaded cells in the current world.
/// Runs in background — progress shown in server log.
/// Usage: /buildnavmesh
/// </summary>
public class BuildNavMeshCommand : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public string[] CommandNames { get; set; } = ["buildnavmesh", "bnm"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "";

    public string GetCommandHelpText() =>
        "Forces navmesh build for ALL loaded cells. Runs in background. " +
        "Use /savenavmesh after completion to persist.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var world = character.ParentWorld;
        if (world?.NavMesh == null)
        {
            Log(messageOutput, "No NavMeshManager on current world instance.");
            return;
        }

        var template = world.Template;
        if (template == null)
        {
            Log(messageOutput, "No world template found.");
            return;
        }

        // Count loaded cells
        var loadedCount = 0;
        for (var cy = 0; cy < template.CellY; cy++)
        for (var cx = 0; cx < template.CellX; cx++)
        {
            var cell = template.Cells[cx, cy];
            if (cell?.Loaded == true)
                loadedCount++;
        }

        Log(messageOutput, $"Queueing navmesh build for {loadedCount} loaded cells (background)...");
        Log(messageOutput, "Watch server log for progress. Use /savenavmesh when done.");

        // Queue all loaded cells for navmesh build in background
        Task.Run(() =>
        {
            var queued = 0;
            for (var cy = 0; cy < template.CellY; cy++)
            for (var cx = 0; cx < template.CellX; cx++)
            {
                var cell = template.Cells[cx, cy];
                if (cell?.Loaded != true)
                    continue;

                world.NavMesh.QueueBuildTile(cell);
                queued++;
            }

            Logger.Info($"[BuildNavMesh] Queued {queued} cells for navmesh build");
        });
    }

    private void Log(IMessageOutput messageOutput, string text)
    {
        CommandManager.SendNormalText(this, messageOutput, text);
        Logger.Info("[BuildNavMesh] " + text);
    }
}
