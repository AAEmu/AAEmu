using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Game.NPChar;

public class NpcSpawnerNpc : Spawner<Npc>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public uint NpcSpawnerTemplateId { get; set; } // spawner template id
    public uint MemberId { get; set; } // npc template id
    public string MemberType { get; set; } // 'Npc'
    public float Weight { get; set; }

    public NpcSpawnerNpc()
    {
    }

    /// <summary>
    /// Creates a new instance of NpcSpawnerNpcs with a Spawner template id (npc_spanwers)
    /// </summary>
    /// <param name="spawnerTemplateId"></param>
    public NpcSpawnerNpc(uint spawnerTemplateId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
    }

    public NpcSpawnerNpc(uint spawnerTemplateId, uint memberId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
        MemberId = memberId;
        MemberType = "Npc";
    }

    public List<Npc> Spawn(NpcSpawner npcSpawner, uint ownerID = 0)
    {
        switch (MemberType)
        {
            case "Npc":
                return SpawnNpc(npcSpawner, ownerID);
            case "NpcGroup":
                return SpawnNpcGroup(npcSpawner, ownerID);
            default:
                throw new InvalidOperationException($"Tried spawning an unsupported line from NpcSpawnerNpc - Id: {Id}");
        }
    }

    private List<Npc> SpawnNpc(NpcSpawner npcSpawner, uint ownerID = 0)
    {
        var npcs = new List<Npc>();
        var npc = NpcManager.Instance.Create(npcSpawner.ParentWorld, 0, MemberId);
        if (npc == null)
        {
            Logger.Warn($"Npc {MemberId}, from spawner Id {npcSpawner.Id} not exist at db. Spawner Position: {npcSpawner.Position}");
            return null;
        }

        npc.ParentWorld = npcSpawner.ParentWorld;
        npc.OwnerId = ownerID;

        npc.RegisterNpcEvents();

        Logger.Trace($"Spawn npc templateId {MemberId} objId {npc.ObjId} from spawnerId {NpcSpawnerTemplateId} at Position: {npcSpawner.Position}");

        if (!npc.CanFly)
        {
            var spawnPos = npcSpawner.Position.AsPositionVector();
            var newZ = WorldManager.Instance.GetHeight(npcSpawner.Position.ZoneId, spawnPos.X, spawnPos.Y, spawnPos.Z);
            if (newZ > 0f)
            {
                if (npcSpawner.Position.Z <= 0f)
                {
                    // No explicit Z in spawner data — use detected ground height
                    npcSpawner.Position.Z = newZ;
                    Logger.Debug($"[SpawnZ] NPC {MemberId} at ({spawnPos.X:F1},{spawnPos.Y:F1}): no Z in file → detected={newZ:F2}");
                }
                else
                {
                    // Explicit Z — snap to ground if close (< 3m), preserving intentional
                    // elevations (balconies, upper floors, etc.)
                    var diff = MathF.Abs(npcSpawner.Position.Z - newZ);
                    if (diff > 0.05f && diff < 3f)
                    {
                        Logger.Debug($"[SpawnZ] NPC {MemberId} at ({spawnPos.X:F1},{spawnPos.Y:F1}): explicit Z={npcSpawner.Position.Z:F2} → ground={newZ:F2} (diff={diff:F2})");
                        npcSpawner.Position.Z = newZ;
                    }
                }
            }
        }

        npc.Transform.ApplyWorldSpawnPosition(npcSpawner.Position);
        if (npc.Transform == null)
        {
            Logger.Error($"Can't spawn npc {MemberId} from spawnerId {NpcSpawnerTemplateId}. Transform is null.");
            return null;
        }

        npc.Transform.InstanceId = npc.Transform.InstanceId;

        if (npc.Ai != null)
        {
            npc.Ai.HomePosition = npc.Transform.World.Position;
            npc.Ai.IdlePosition = npc.Ai.HomePosition;
            npc.Ai.GoToSpawn();
        }

        npc.Spawner = npcSpawner;
        npc.Spawner.RespawnTime = (int)Random.Shared.Next(npc.Spawner.Template.SpawnDelayMin, npc.Spawner.Template.SpawnDelayMax);
        npc.Spawn();

        var world = WorldManager.Instance.GetWorld(npc.Transform.InstanceId);
        world.Events.OnUnitSpawn(world, new OnUnitSpawnArgs { Npc = npc });
        npc.Simulation = new Simulation(npc);

        if (npc.Ai != null && !string.IsNullOrWhiteSpace(npcSpawner.FollowPath))
        {
            if (!npc.Ai.LoadAiPathPoints(npcSpawner.FollowPath, false))
                Logger.Warn($"Failed to load {npcSpawner.FollowPath} for NPC {npc.TemplateId} ({npc.ObjId})");
        }

        npcs.Add(npc);
        return npcs;
    }

    private List<Npc> SpawnNpcGroup(NpcSpawner npcSpawner, uint ownerID = 0)
    {
        return SpawnNpc(npcSpawner, ownerID);
    }
}
