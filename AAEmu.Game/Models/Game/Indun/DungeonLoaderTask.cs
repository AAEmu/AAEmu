using System.Threading;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks;
using NLog;

namespace AAEmu.Game.Models.Game.Indun;

public class DungeonLoaderTask(WorldTemplate worldTemplate, Dungeon dungeon, uint dungeonInstanceId, Character notifyPlayer) : Task
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Execute()
    {
        // TODO: Channel related things
        // Thread.Sleep(10000); // debug delay 

        // Create a new WorldInstance linked to this Dungeon
        if (dungeon.World == null)
        {
            Logger.Debug($"[???-{worldTemplate.Name}({worldTemplate.Id})] Creating new dungeon instance of  ...");
            dungeon.World = WorldManager.Instance.CreateWorldInstance(worldTemplate, 0, true, dungeonInstanceId, notifyPlayer);
            dungeon.World.DungeonInstance = dungeon;
            Thread.Sleep(1000);
            Logger.Info($"[{dungeon.World})] New Dungeon instance created!");
        }


        // Spawn all world elements for this dungeon
        Logger.Debug($"[{dungeon.World})] Spawning game objects Npc, Doodad, Slave, Gimmick...");
        dungeon.World.SpawnManager.SpawnAll();
        Logger.Debug($"[{dungeon.World})] Finished spawning game objects Npc, Doodad, Slave, Gimmick...");

        // Register events
        dungeon.RegisterIndunEvents();
        Logger.Info($"[{dungeon.World})] Dungeon instance ready!");
        
        dungeon.FinishedLoading = true;

        Thread.Sleep(1000);

        // Spawn players
        if (dungeon.EnterRequests.Count > 0)
        {
            Logger.Info($"[{dungeon.World})] Moving players to dungeon instance ...");
            foreach (var dungeonEnterRequestPlayer in dungeon.EnterRequests)
            {
                if (dungeonEnterRequestPlayer?.IsOnline ?? false)
                {
                    dungeon.AddPlayer(dungeonEnterRequestPlayer);
                }
            }

            dungeon.EnterRequests.Clear();
        }
    }
}
