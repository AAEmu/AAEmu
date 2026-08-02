using System.Numerics;

using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.World;
using NLog;

using static System.String;
using Task = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.Game.Core.Managers;

public class GimmickManager(WorldInstance parentWorld)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public WorldInstance ParentWorld { get; init; } = parentWorld;
    internal readonly Dictionary<uint, Gimmick> _activeGimmicks = [];
    private const double Delay = 50;
    //private const double DelayInit = 1;
    private Task GimmickTickTask { get; set; }
    private DateTime LastCheck { get; set; } = DateTime.MinValue;

    private const uint NoObjectId = 0;

    /// <summary>
    /// Create for spawning elevators
    /// </summary>
    /// <param name="objectId"></param>
    /// <param name="templateId"></param>
    /// <param name="spawner"></param>
    /// <returns></returns>
    public Gimmick Create(uint objectId, uint templateId, GimmickSpawner spawner)
    {
        /*
         * for elevators: templateId=0 and Template=null, but EntityGuid is used
         */

        var template = GimmickGameData.Instance.GetGimmickTemplate(templateId);
        if (template == null && templateId != 0)
            return null;
        var gimmick = new Gimmick
        {
            ParentWorld = ParentWorld,
            Template = template,
            ModelPath = template?.ModelPath ?? Empty,
            EntityGuid = template == null ? spawner.EntityGuid : 0,
            ObjId = objectId > 0 ? objectId : NonUnitObjectIdManager.Instance.GetNextId(),
            GimmickId = (ushort)GimmickIdManager.Instance.GetNextId(),
            Spawner = spawner,
            TemplateId = templateId,
            Faction = new SystemFaction()
        };
        gimmick.Transform.ApplyWorldSpawnPosition(spawner.Position);
        gimmick.Vel = new Vector3(0f, 0f, 0f);
        var spawnRotation = new Quaternion(spawner.RotationX, spawner.RotationY, spawner.RotationZ, spawner.RotationW);
        // Apply Gimmick setting's rotation to the GameObject.Transform
        gimmick.Transform.Local.ApplyFromQuaternion(spawnRotation);
        gimmick.SetScale(spawner.Scale);

        if (gimmick.Transform.World.IsOrigin())
        {
            Logger.Error($"Can't spawn gimmick {templateId}");
            return null;
        }

        gimmick.Spawn(); // adding to the world
        AddActiveGimmick(gimmick);

        return gimmick;
    }

    public void AddActiveGimmick(Gimmick gimmick)
    {
        // Attach movement handlers based on settings
        if (gimmick.TemplateId == 0 && gimmick.EntityGuid > 0 &&
            gimmick.Spawner?.TopZ > gimmick.Spawner?.BottomZ)
        {
            // Level entities with a real vertical span: lifts. Without the span check a
            // physicalized prop would be handed the elevator handler and driven to Z 0.
            gimmick.MovementHandler = new GimmickMovementElevator(gimmick);
        }
        else
            // TODO: Add decent Physics system to handle movement
        if (gimmick.TemplateId == 37)
        {
            // Recovered Treasure Chest
            gimmick.MovementHandler = new GimmickMovementFloatToSurface(gimmick);
        }

        gimmick.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;
        lock (_activeGimmicks)
            _activeGimmicks.TryAdd(gimmick.ObjId, gimmick);
    }

    /// <summary>
    /// Whether this world simulates the gimmick with the given object id.
    /// </summary>
    /// <remarks>
    /// The World spawns the level's gimmicks and drives them itself, broadcasting their movement
    /// from Gimmick.GimmickTick, while also announcing them to the zone. A zone movement report for
    /// one of those would therefore be a second update for a gimmick already being driven here.
    /// </remarks>
    public bool OwnsGimmick(uint objId)
    {
        lock (_activeGimmicks)
            return _activeGimmicks.ContainsKey(objId);
    }

    /// <summary>
    /// Applies a client's grasp interaction to a visible, content-authorized gimmick.
    /// Interacting with the held gimmick releases it; selecting another releases the prior hold
    /// before assigning the new one. A gimmick already held by another unit remains authoritative.
    /// </summary>
    public bool Interact(Character character, uint gimmickObjId)
    {
        if (character?.ParentWorld != ParentWorld)
            return false;

        var visibleGimmick = WorldManager.GetAround<Gimmick>(character)
            .FirstOrDefault(gimmick => gimmick.ObjId == gimmickObjId);
        if (visibleGimmick?.Template?.Graspable != true)
            return false;

        List<(Gimmick Gimmick, uint GrasperUnitId, bool Grasped)> changes = [];
        lock (_activeGimmicks)
        {
            if (!_activeGimmicks.TryGetValue(gimmickObjId, out var gimmick) ||
                !ReferenceEquals(gimmick, visibleGimmick))
                return false;

            if (gimmick.GrasperUnitId == character.ObjId)
            {
                gimmick.GrasperUnitId = NoObjectId;
                changes.Add((gimmick, character.ObjId, false));
            }
            else
            {
                if (gimmick.GrasperUnitId != NoObjectId)
                    return false;

                foreach (var heldGimmick in _activeGimmicks.Values
                             .Where(candidate => candidate.GrasperUnitId == character.ObjId))
                {
                    heldGimmick.GrasperUnitId = NoObjectId;
                    changes.Add((heldGimmick, character.ObjId, false));
                }

                gimmick.GrasperUnitId = character.ObjId;
                changes.Add((gimmick, character.ObjId, true));
            }
        }

        if (changes.Count > 0 && changes[^1].Grasped)
            WorldIntegration.ReleaseZoneGimmickGrasps?.Invoke(character);

        foreach (var change in changes)
            PublishGraspState(change.Gimmick, change.GrasperUnitId, change.Grasped);
        return true;
    }

    /// <summary>Releases every gimmick held by a unit that is leaving this world.</summary>
    public void ReleaseGrasps(uint grasperUnitId)
    {
        if (grasperUnitId == NoObjectId)
            return;

        List<Gimmick> released = [];
        lock (_activeGimmicks)
        {
            foreach (var gimmick in _activeGimmicks.Values
                         .Where(candidate => candidate.GrasperUnitId == grasperUnitId))
            {
                gimmick.GrasperUnitId = NoObjectId;
                released.Add(gimmick);
            }
        }

        foreach (var gimmick in released)
            PublishGraspState(gimmick, grasperUnitId, false);
    }

    public void RemoveActiveGimmick(Gimmick gimmick)
    {
        var grasperUnitId = NoObjectId;
        lock (_activeGimmicks)
        {
            grasperUnitId = gimmick.GrasperUnitId;
            gimmick.GrasperUnitId = NoObjectId;
            _activeGimmicks.Remove(gimmick.ObjId);
        }

        if (grasperUnitId != NoObjectId)
            PublishGraspState(gimmick, grasperUnitId, false);
    }

    private static void PublishGraspState(Gimmick gimmick, uint grasperUnitId, bool grasped)
    {
        // Both object-id allocators used here are bounded to the client's signed i32 wire range.
        gimmick.BroadcastPacket(
            new SCGimmickGraspedPacket((int)gimmick.ObjId, (int)grasperUnitId, grasped), false);

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayGimmickGraspedToZone?.Invoke(
                gimmick.Transform.ZoneId, gimmick.ObjId, grasperUnitId, grasped);
    }

    /// <summary>
    /// Create for spawning projectiles
    /// </summary>
    /// <param name="templateId"></param>
    /// <returns></returns>
    public Gimmick Create(uint templateId)
    {
        var template = GimmickGameData.Instance.GetGimmickTemplate(templateId);
        if (template == null) { return null; }

        var gimmick = new Gimmick
        {
            ParentWorld = ParentWorld,
            ObjId = NonUnitObjectIdManager.Instance.GetNextId(),
            GimmickId = (ushort)GimmickIdManager.Instance.GetNextId(),
            Spawner = new GimmickSpawner(ParentWorld),
            Template = template,
            TemplateId = template.Id,
            Faction = new SystemFaction(),
            ModelPath = template.ModelPath,
        };

        return gimmick;
    }

    public void Initialize()
    {
        Logger.Info("GimmickTickTask: Started");
        TickManager.Instance.OnTick.Subscribe(GimmickTick, TimeSpan.FromMilliseconds(Delay), true);
    }

    /// <summary>
    /// Callback function for global gimmick ticks
    /// </summary>
    /// <param name="delta"></param>
    private void GimmickTick(TimeSpan delta)
    {
        var activeGimmicks = GetActiveGimmicks();
        foreach (var gimmick in activeGimmicks)
        {
            gimmick.GimmickTick(delta);
        }
    }

    public Gimmick[] GetActiveGimmicks()
    {
        lock (_activeGimmicks)
        {
            return _activeGimmicks.Values.ToArray();
        }
    }
}
