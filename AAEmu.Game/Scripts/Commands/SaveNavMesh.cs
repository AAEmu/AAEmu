using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

using NLog;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Saves the current world's navmesh to Data/NavMesh/{worldName}.bin.
/// On next server boot, the navmesh is loaded from this cache file instead of
/// being rebuilt from BAI/brush geometry, making startup much faster.
///
/// Usage: /savenavmesh          — saves cache for current world
///        /savenavmesh clear    — deletes the cache (forces rebuild on next boot)
/// </summary>
public class SaveNavMeshCommand : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public string[] CommandNames { get; set; } = ["savenavmesh", "snm"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "[clear]";

    public string GetCommandHelpText() =>
        "Saves the navmesh to disk for automatic loading on next boot. " +
        "Use 'clear' to delete the cache and force rebuild.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var world = character.ParentWorld;
        if (world?.NavMesh == null)
        {
            Log(messageOutput, "No NavMeshManager on current world instance.");
            return;
        }

        var cachePath = world.NavMesh.GetCachePath();

        // Handle "clear" subcommand
        if (args.Length > 0 && args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
                Log(messageOutput, $"Cache deleted: {cachePath}");
                Log(messageOutput, "NavMesh will be rebuilt from geometry on next boot.");
            }
            else
            {
                Log(messageOutput, "No cache file exists.");
            }
            return;
        }

        if (!world.NavMesh.HasData)
        {
            Log(messageOutput, "NavMesh has no data to save. Wait for cells to finish loading.");
            return;
        }

        Log(messageOutput, $"Saving navmesh ({world.NavMesh.TileCount} tiles) to cache...");

        if (world.NavMesh.SaveCache())
        {
            Log(messageOutput, $"Saved: {cachePath}");
            Log(messageOutput, "Next boot will load from cache (skipping geometry build).");
        }
        else
        {
            Log(messageOutput, "Failed to save navmesh cache.");
        }
    }

    private void Log(IMessageOutput messageOutput, string text)
    {
        CommandManager.SendNormalText(this, messageOutput, text);
        Logger.Info("[SaveNavMesh] " + text);
    }
}
