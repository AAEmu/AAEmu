using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Debug/GM helpers for BAI-derived ship static barriers (GeoDataMode).
/// Uses only <see cref="WorldInstance"/> public barrier helpers so Roslyn script compilation (compiler-check) succeeds.
/// </summary>
public sealed class ShipBarrierCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["shipbarrier", "sb"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "reset [world|cell] [ingest] | status";
    }

    public string GetCommandHelpText()
    {
        return "Ship barrier debug utilities.\n" +
               "reset world: clears all BAI-ingested ship barriers and ingested-cells cache for the current world.\n" +
               "reset cell: clears all barriers+cache (same as world for now) and optionally ingests the current cell.\n" +
               "Add \"ingest\" to immediately ingest current + neighbor cells.\n" +
               "status: prints current barrier/cache counters.\n" +
               "Examples:\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " status\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " reset world ingest\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " reset cell ingest";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var world = character.ParentWorld;
        if (world is null)
        {
            CommandManager.SendErrorText(this, messageOutput, "No world instance.");
            return;
        }

        if (world.ShipStaticBarriers is null)
        {
            CommandManager.SendErrorText(this, messageOutput,
                "ShipStaticBarriers not initialized (GeoDataMode off for this instance?).");
            return;
        }

        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "status":
                SendStatus(world, messageOutput);
                return;

            case "reset":
                HandleReset(character, world, args, messageOutput);
                return;

            default:
                CommandManager.SendDefaultHelpText(this, messageOutput);
                return;
        }
    }

    private void HandleReset(Character character, WorldInstance world, string[] args, IMessageOutput messageOutput)
    {
        var mode = args.Length >= 2 ? args[1].ToLowerInvariant() : "world";
        var doIngest = args.Any(a => a.Equals("ingest", StringComparison.OrdinalIgnoreCase));

        if (mode != "world" && mode != "cell")
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: reset [world|cell] [ingest]");
            return;
        }

        world.ClearShipStaticBarriersAndBaiIngest(out var beforeBarriers, out var beforeCells);

        CommandManager.SendNormalText(this, messageOutput,
            $"Cleared ship barriers for {world}. Barriers {beforeBarriers}→0, ingested cells {beforeCells}→0.");

        if (!doIngest)
            return;

        var pos = character.Transform.World.Position;
        var (cx, cy) = pos.ToCellIndex();

        // Ingest current + neighbors to make the effect immediate for nearby navigation.
        var ingested = 0;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var tx = cx + dx;
            var ty = cy + dy;
            var preCount = world.GetShipStaticBarrierCountLocked();
            world.EnsureShipStaticBarrierBaiCell(tx, ty);
            var postCount = world.GetShipStaticBarrierCountLocked();
            if (postCount > preCount)
                ingested++;
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"Triggered ingest around cell ({cx},{cy}); cells that added barriers: {ingested}.");
    }

    private void SendStatus(WorldInstance world, IMessageOutput messageOutput)
    {
        world.GetShipStaticBarrierDebugCounts(out var barriers, out var cells);

        CommandManager.SendNormalText(this, messageOutput,
            $"World={world} GeoDataMode={AppConfiguration.Instance.World.GeoDataMode} barriers={barriers} ingestedCells={cells}");
    }
}
