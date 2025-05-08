using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks;

using NLog;

using static System.String;

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
            ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId(),
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
        if ((gimmick.TemplateId == 0) && (gimmick.EntityGuid > 0))
        {
            // Elevators defined in gimmick_spawns.json
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
        _activeGimmicks.TryAdd(gimmick.ObjId, gimmick);
    }

    public void RemoveActiveGimmick(Gimmick gimmick)
    {
        _activeGimmicks.Remove(gimmick.ObjId);
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
            ObjId = ObjectIdManager.Instance.GetNextId(),
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
        Logger.Warn("GimmickTickTask: Started");
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

    private Gimmick[] GetActiveGimmicks()
    {
        lock (_activeGimmicks)
        {
            return _activeGimmicks.Values.ToArray();
        }
    }
}

