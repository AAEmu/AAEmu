#nullable enable

using System.Collections.Concurrent;
using System.Numerics;

using AAEmu.Game.Physics;

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.Debug;

/// <summary>
/// EN: Dev-only ship physics debug: gimmick markers (mass box, axes, shore probes) and chat lines. Physics tuning lives in <see cref="ShipController.ShipMotionDefaults"/>, <see cref="ShipController.ShipMassBoxDefaults"/>, <see cref="ShipShipInteraction.ShipHullPairDefaults"/>, <see cref="ShipShoreInteraction.ShorePhysicsDefaults"/> — not here.
/// RU: Только дебаг корабельной физики: маркеры-гиммики (бокс, оси, берег) и строки в чат. Тюнинг физики — в <see cref="ShipController.ShipMotionDefaults"/>, <see cref="ShipController.ShipMassBoxDefaults"/>, <see cref="ShipShipInteraction.ShipHullPairDefaults"/>, <see cref="ShipShoreInteraction.ShorePhysicsDefaults"/>, не в этом классе.
/// EN: Marker template ids come from table <c>gimmicks</c> — see <see cref="CornerMarkerTemplateId"/>, <see cref="ShoreMarkerTemplateId"/>, axis ids; set id to <c>0</c> or use <see cref="Enabled"/> / <see cref="AxisMarkersEnabled"/> to hide visuals.
/// RU: Id шаблонов маркеров — таблица <c>gimmicks</c> (углы, берег, оси); <c>0</c> или флаги <see cref="Enabled"/> / <see cref="AxisMarkersEnabled"/> отключают визуал.
/// </summary>
public static class ShipTuningDebug
{
    /// <summary>
    /// EN: Master switch for gimmick markers and for per-tick debug (corners, axes, speed/box chat in <see cref="TickShip"/>). Callback chat (ship↔ship detail, shore latch/penetration) uses the separate flags below and may still run when this is false.
    /// RU: Главный выключатель маркеров и тика <see cref="TickShip"/> (углы, оси, чат скорости/бокса). Чат из колбэков (детали ship↔shore, берег) — отдельные флаги ниже; часть может работать при false.
    /// </summary>
    public static bool Enabled => GetEnabled();
    private static bool GetEnabled() => false;

    /// <summary>
    /// EN: Minimum effective access level to receive chat debug messages.
    /// RU: Минимальный effective access level для получения debug-сообщений в чат.
    /// </summary>
    public static int MinAccessLevel => GetMinAccessLevel();
    private static int GetMinAccessLevel() => 80;

    /// <summary>
    /// EN: Throttle detailed messages (ms) per ship.
    /// RU: Троттлинг подробных сообщений (мс) на корабль.
    /// </summary>
    public static int ThrottleMsPerShip => GetThrottleMsPerShip();
    private static int GetThrottleMsPerShip() => 3000;

    /// <summary>
    /// EN: Enable/disable `[ShipBox]` chat line (markers still draw).
    /// RU: Включить/выключить строку `[ShipBox]` в чате (маркеры остаются).
    /// </summary>
    public static bool ShipBoxChatEnabled => GetShipBoxChatEnabled();
    private static bool GetShipBoxChatEnabled() => false;

    /// <summary>
    /// EN: Enable/disable `[ShipSpeed]` chat line (escape/ground diagnostics).
    /// RU: Включить/выключить строку `[ShipSpeed]` в чате (диагностика скорости/мели).
    /// </summary>
    public static bool ShipSpeedChatEnabled => GetShipSpeedChatEnabled();
    private static bool GetShipSpeedChatEnabled() => false;

    /// <summary>
    /// EN: Eight corner markers on the mass-box (table <c>gimmicks</c>). Default id <c>65</c> (toy.flare). To disable only corners: return <c>0</c> (or turn off all visuals via <see cref="Enabled"/>).
    /// RU: Восемь маркеров углов mass-box (таблица <c>gimmicks</c>). По умолчанию id <c>65</c> (toy.flare). Выключить только углы: <c>return 0</c> (или все маркеры — <see cref="Enabled"/>).
    /// </summary>
    public static uint CornerMarkerTemplateId => GetCornerMarkerTemplateId();
    private static uint GetCornerMarkerTemplateId() => 0; // toy.flare

    /// <summary>
    /// EN: Shore probe/contact markers (table <c>gimmicks</c>). Return <c>0</c> to hide. Shore physics uses <see cref="ShipShoreInteraction.ShorePhysicsDefaults"/> regardless of this class.
    /// RU: Маркеры берега/проб (таблица <c>gimmicks</c>). <c>return 0</c> — скрыть. Физика берега всегда из <see cref="ShipShoreInteraction.ShorePhysicsDefaults"/>.
    /// </summary>
    public static uint ShoreMarkerTemplateId => GetShoreMarkerTemplateId();
    private static uint GetShoreMarkerTemplateId() => 0;

    /// <summary>
    /// EN: Axis marker for +Length (local +Z of the physics box). Default id <c>16</c> (firecracker_green). To disable all axis markers at once: <see cref="AxisMarkersEnabled"/> = false; per-axis: return <c>0</c> here.
    /// RU: Маркер оси +Length (локальная +Z бокса). По умолчанию id <c>16</c> (firecracker_green). Все оси сразу: <see cref="AxisMarkersEnabled"/> = false; только эта ось: <c>return 0</c>.
    /// </summary>
    public static uint AxisLengthMarkerTemplateId => GetAxisLengthMarkerTemplateId();
    private static uint GetAxisLengthMarkerTemplateId() => 0; // firecracker_green

    /// <summary>
    /// EN: Axis marker for +Beam (local +X). Default id <c>18</c> (firecracker_blue). Disable with <see cref="AxisMarkersEnabled"/> or return <c>0</c>.
    /// RU: Маркер оси +Beam (локальная +X). По умолчанию id <c>18</c> (firecracker_blue). Выключение — <see cref="AxisMarkersEnabled"/> или <c>return 0</c>.
    /// </summary>
    public static uint AxisBeamMarkerTemplateId => GetAxisBeamMarkerTemplateId();
    private static uint GetAxisBeamMarkerTemplateId() => 0; // firecracker_blue (assumed adjacent id)

    /// <summary>
    /// EN: Axis marker for +Up (local +Y). Default id <c>28</c> (same id as shore marker; change if you need a distinct look). Disable with <see cref="AxisMarkersEnabled"/> or return <c>0</c>.
    /// RU: Маркер оси +Up (локальная +Y). По умолчанию id <c>28</c> (тот же id, что у берега; поменяй, если нужен другой вид). Выключение — <see cref="AxisMarkersEnabled"/> или <c>return 0</c>.
    /// </summary>
    public static uint AxisUpMarkerTemplateId => GetAxisUpMarkerTemplateId();
    private static uint GetAxisUpMarkerTemplateId() => 0; // fallback: visible marker

    /// <summary>
    /// EN: Marker scale.
    /// RU: Масштаб маркеров.
    /// </summary>
    public static float MarkerScale => GetMarkerScale();
    private static float GetMarkerScale() => 0.35f;

    /// <summary>
    /// EN: Offset axis gimmicks outward along each local axis (meters) so they sit outside the hull box; debug visuals only.
    /// RU: Вынести маркеры осей наружу вдоль локальных осей (м), только для видимости дебага.
    /// </summary>
    public static float AxisMarkerExtraMeters => GetAxisMarkerExtraMeters();
    private static float GetAxisMarkerExtraMeters() => 0f;

    /// <summary>
    /// EN: If true, draw markers only for ships with a driver.
    /// RU: Если true — рисовать маркеры только для кораблей с водителем.
    /// </summary>
    public static bool DrawOnlyWhenDriven => GetDrawOnlyWhenDriven();
    private static bool GetDrawOnlyWhenDriven() => false;

    /// <summary>
    /// EN: Master switch for length/beam/up axis gimmick markers (independent of corner markers).
    /// RU: Общий выключатель маркеров осей length/beam/up (независимо от углов бокса).
    /// </summary>
    public static bool AxisMarkersEnabled => GetAxisMarkersEnabled();
    private static bool GetAxisMarkersEnabled() => false;

    /// <summary>
    /// EN: Chat: ship↔ship hull contact started/ended (from <see cref="TickShip"/>; requires <see cref="Enabled"/>).
    /// RU: Чат: начало/конец контакта корпусов ship↔ship (через <see cref="TickShip"/>, нужен <see cref="Enabled"/>).
    /// </summary>
    public static bool ShipShipContactLatchChatEnabled => GetShipShipContactLatchChatEnabled();
    private static bool GetShipShipContactLatchChatEnabled() => false;

    /// <summary>
    /// EN: Chat: throttled ship↔ship resolve detail (penetration, normal, impact speed) from <see cref="OnResolvedShipPair"/>.
    /// RU: Чат: детали резолва ship↔ship (проникновение, нормаль, скорость удара), троттлинг, из <see cref="OnResolvedShipPair"/>.
    /// </summary>
    public static bool ShipShipResolveDetailChatEnabled => GetShipShipResolveDetailChatEnabled();
    private static bool GetShipShipResolveDetailChatEnabled() => false;

    /// <summary>
    /// EN: Chat: shore latch ground / back in water (from <see cref="OnShoreLatchChanged"/>).
    /// RU: Чат: береговой latch «на мели» / «снова в воде».
    /// </summary>
    public static bool ShoreLatchChatEnabled => GetShoreLatchChatEnabled();
    private static bool GetShoreLatchChatEnabled() => false;

    /// <summary>
    /// EN: Chat: throttled shore penetration depth while aground (from <see cref="OnShoreProbe"/>).
    /// RU: Чат: глубина проникновения в мель (троттлинг), из <see cref="OnShoreProbe"/>.
    /// </summary>
    public static bool ShorePenetrationChatEnabled => GetShorePenetrationChatEnabled();
    private static bool GetShorePenetrationChatEnabled() => false;

    private static readonly ConcurrentDictionary<uint, long> _lastDetailMsgAtMs = new();
    private static readonly ConcurrentDictionary<uint, long> _lastAxesMsgAtMs = new();
    private static readonly ConcurrentDictionary<uint, long> _lastShorePenMsgAtMs = new();
    private static readonly ConcurrentDictionary<uint, long> _lastSpeedMsgAtMs = new();

    private static readonly ConcurrentDictionary<uint, MarkerSet> _shipCornerMarkers = new();
    private static readonly ConcurrentDictionary<uint, MarkerSet> _shipAxisLenMarkers = new();
    private static readonly ConcurrentDictionary<uint, MarkerSet> _shipAxisBeamMarkers = new();
    private static readonly ConcurrentDictionary<uint, MarkerSet> _shipAxisUpMarkers = new();
    private static readonly ConcurrentDictionary<uint, MarkerSet> _shoreMarkers = new();

    // ship↔ship contact latch based on ResolvePair events
    private static readonly ConcurrentDictionary<uint, long> _shipContactUntilMs = new();
    private static readonly ConcurrentDictionary<uint, bool> _shipContactLatched = new();

    private sealed class MarkerSet
    {
        public readonly GimmickSpawner[] Spawners;
        public readonly Gimmick?[] Markers;
        public uint ZoneId;
        public uint TemplateId;

        public MarkerSet(int count)
        {
            Spawners = new GimmickSpawner[count];
            Markers = new Gimmick?[count];
        }
    }

    /// <summary>
    /// EN: Per-ship debug tick. Updates corner markers and ship↔ship latch messages.
    /// RU: Дебаг-тик на корабль. Обновляет углы бокса и latch-сообщения ship↔ship.
    /// </summary>
    public static void TickShip(Slave ship)
    {
        if (ship is null)
            return;

        if (!Enabled)
        {
            DespawnAll(ship.Id);
            return;
        }

        if (DrawOnlyWhenDriven && TryGetDriver(ship) is null)
        {
            DespawnAll(ship.Id);
            return;
        }

        UpdateShipCornerMarkers(ship);
        UpdateShipAxisMarkers(ship);
        UpdateShipSpeedDebug(ship);
        UpdateShipContactLatch(ship);
    }

    private static void UpdateShipSpeedDebug(Slave ship)
    {
        if (!ShipSpeedChatEnabled)
            return;

        var driver = TryGetDriver(ship);
        if (driver is null || !CanReceive(driver))
            return;

        var nowMs = Environment.TickCount64;
        var throttle = Math.Max(250, ThrottleMsPerShip);
        var last = _lastSpeedMsgAtMs.GetOrAdd(ship.Id, 0);
        if (nowMs - last < throttle)
            return;
        _lastSpeedMsgAtMs[ship.Id] = nowMs;

        var isGrounded = (ship.CachedFloorLevel > ship.CachedWaterSurface) || ship.GroundContactLatched;
        var escapeThrottleSign = ship.GroundedByStern ? 1 : -1;
        var isEscapeInputOnGround = isGrounded && ship.ThrottleRequest != 0 && Math.Sign(ship.ThrottleRequest) == escapeThrottleSign;

        var cap = ShipController.ShipMotionDefaults.GroundEscapeMaxSpeedAbs;

        driver.SendDebugMessage(
            $"[ShipSpeed] ship={ship.ObjId} v={ship.Speed:F2} grounded={isGrounded} latched={ship.GroundContactLatched} byStern={ship.GroundedByStern} thrReq={ship.ThrottleRequest} escape={isEscapeInputOnGround} escapeCap={cap:F2}");
    }

    private static void UpdateShipAxisMarkers(Slave ship)
    {
        if (!AxisMarkersEnabled)
        {
            DespawnMarkers(_shipAxisLenMarkers, ship.Id);
            DespawnMarkers(_shipAxisBeamMarkers, ship.Id);
            DespawnMarkers(_shipAxisUpMarkers, ship.Id);
            return;
        }

        var lenTemplateId = AxisLengthMarkerTemplateId;
        var beamTemplateId = AxisBeamMarkerTemplateId;
        var upTemplateId = AxisUpMarkerTemplateId;
        if ((lenTemplateId == 0 && beamTemplateId == 0 && upTemplateId == 0) || ship.ParentWorld is null)
        {
            DespawnMarkers(_shipAxisLenMarkers, ship.Id);
            DespawnMarkers(_shipAxisBeamMarkers, ship.Id);
            DespawnMarkers(_shipAxisUpMarkers, ship.Id);
            return;
        }

        var rb = ship.RigidBody;
        var model = ship.ShipController?.ShipModel;
        if (rb is null || model is null)
        {
            DespawnMarkers(_shipAxisLenMarkers, ship.Id);
            DespawnMarkers(_shipAxisBeamMarkers, ship.Id);
            DespawnMarkers(_shipAxisUpMarkers, ship.Id);
            return;
        }

        var zoneId = ship.Transform.ZoneId;
        MarkerSet? setLen = null;
        if (lenTemplateId != 0)
        {
            setLen = _shipAxisLenMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(1));
            if (setLen.ZoneId != zoneId || setLen.TemplateId != lenTemplateId)
            {
                DespawnMarkers(_shipAxisLenMarkers, ship.Id);
                setLen = _shipAxisLenMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(1));
                setLen.ZoneId = zoneId;
                setLen.TemplateId = lenTemplateId;
            }
        }
        else
        {
            DespawnMarkers(_shipAxisLenMarkers, ship.Id);
        }

        MarkerSet? setBeam = null;
        if (beamTemplateId != 0)
        {
            setBeam = _shipAxisBeamMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(1));
            if (setBeam.ZoneId != zoneId || setBeam.TemplateId != beamTemplateId)
            {
                DespawnMarkers(_shipAxisBeamMarkers, ship.Id);
                setBeam = _shipAxisBeamMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(1));
                setBeam.ZoneId = zoneId;
                setBeam.TemplateId = beamTemplateId;
            }
        }
        else
        {
            DespawnMarkers(_shipAxisBeamMarkers, ship.Id);
        }

        MarkerSet? setUp = null;
        if (upTemplateId != 0)
        {
            setUp = _shipAxisUpMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(1));
            if (setUp.ZoneId != zoneId || setUp.TemplateId != upTemplateId)
            {
                DespawnMarkers(_shipAxisUpMarkers, ship.Id);
                setUp = _shipAxisUpMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(1));
                setUp.ZoneId = zoneId;
                setUp.TemplateId = upTemplateId;
            }
        }
        else
        {
            DespawnMarkers(_shipAxisUpMarkers, ship.Id);
        }

        // Same basis as UpdateShipCornerMarkers (ShipController.Build mapping).
        var scale = MathF.Max(0.01f, ship.Scale);
        var hx = model.MassBoxSizeX * scale * 0.5f;
        var hy = ShipController.ShipMassBoxDefaults.GetSizeZ(model.MassBoxSizeZ) * scale * 0.5f;
        var hz = model.MassBoxSizeY * scale * 0.5f;

        // Use the already-synced Transform rotation (derived from physics via SyncTransformWithRigidBody)
        // to avoid left/right inversion from phys<->game basis reflections.
        var rotGame = GetTransformRotationMatrix(ship.Transform.Local.Rotation);

        var posGame0 = PhysToGame(rb.Position);
        var offsetLocalPhys = new JVector(
            model.MassCenterX * scale,
            ShipController.ShipMassBoxDefaults.GetCenterZ(model.MassCenterZ, model.MassBoxSizeZ) * scale,
            model.MassCenterY * scale);
        var offsetLocalGame = PhysVecToGame(offsetLocalPhys);
        var centerGame = posGame0 + Vector3.TransformNormal(offsetLocalGame, rotGame);

        var extra = MathF.Max(0f, AxisMarkerExtraMeters);

        // +Length marker: local +Z of the physics box (uses MassBoxSizeY via ShipController.Build third parameter).
        var localLenGame = PhysVecToGame(new JVector(0f, 0f, hz + extra));
        var posLen = centerGame + Vector3.TransformNormal(localLenGame, rotGame);

        // +Beam marker: local +X of the physics box (uses MassBoxSizeX via ShipController.Build first parameter).
        var localBeamGame = PhysVecToGame(new JVector(hx + extra, 0f, 0f));
        var posBeam = centerGame + Vector3.TransformNormal(localBeamGame, rotGame);

        // +Up marker: local +Y of the physics box (uses MassBoxSizeZ via ShipController.Build second parameter).
        var localUpGame = PhysVecToGame(new JVector(0f, hy + extra, 0f));
        var posUp = centerGame + Vector3.TransformNormal(localUpGame, rotGame);

        // Compute vertical extent of the OBB in world (game Z is up).
        var minZ = float.PositiveInfinity;
        var maxZ = float.NegativeInfinity;
        Span<JVector> localCornersPhys =
        [
            new JVector(+hx, +hy, +hz),
            new JVector(+hx, +hy, -hz),
            new JVector(+hx, -hy, +hz),
            new JVector(+hx, -hy, -hz),
            new JVector(-hx, +hy, +hz),
            new JVector(-hx, +hy, -hz),
            new JVector(-hx, -hy, +hz),
            new JVector(-hx, -hy, -hz),
        ];
        for (var i = 0; i < 8; i++)
        {
            var cGame = PhysVecToGame(localCornersPhys[i]);
            var w = centerGame + Vector3.TransformNormal(cGame, rotGame);
            if (w.Z < minZ) minZ = w.Z;
            if (w.Z > maxZ) maxZ = w.Z;
        }

        if (setLen != null)
            UpdateMarker(ship.ParentWorld, setLen, 0, lenTemplateId, zoneId, posLen);
        if (setBeam != null)
            UpdateMarker(ship.ParentWorld, setBeam, 0, beamTemplateId, zoneId, posBeam);
        if (setUp != null)
            UpdateMarker(ship.ParentWorld, setUp, 0, upTemplateId, zoneId, posUp);

        // One short line to the driver's chat (sizes + centers).
        var driver = TryGetDriver(ship);
        if (driver is null || !CanReceive(driver) || !ShipBoxChatEnabled)
            return;

        var nowMs = Environment.TickCount64;
        var throttle = Math.Max(250, ThrottleMsPerShip);
        var last = _lastAxesMsgAtMs.GetOrAdd(ship.Id, 0);
        if (nowMs - last < throttle)
            return;
        _lastAxesMsgAtMs[ship.Id] = nowMs;

        var waterZ = ship.CachedWaterSurface;
        driver.SendDebugMessage(
            $"[ShipBox] ship={ship.ObjId} sizeXYZ=({model.MassBoxSizeX:F2},{model.MassBoxSizeY:F2},{model.MassBoxSizeZ:F2}) centerXYZ=({model.MassCenterX:F2},{model.MassCenterY:F2},{model.MassCenterZ:F2}) z=[{minZ:F2}..{maxZ:F2}] waterZ={waterZ:F2}");
    }

    private static void UpdateShipContactLatch(Slave ship)
    {
        var nowMs = Environment.TickCount64;
        var hasContact = _shipContactUntilMs.TryGetValue(ship.Id, out var untilMs) && untilMs > nowMs;
        if (!hasContact)
            _shipContactUntilMs.TryRemove(ship.Id, out _);

        var prev = _shipContactLatched.TryGetValue(ship.Id, out var v) && v;
        if (prev == hasContact)
            return;

        _shipContactLatched[ship.Id] = hasContact;
        if (!ShipShipContactLatchChatEnabled)
            return;

        var driver = TryGetDriver(ship);
        if (driver is null || !CanReceive(driver))
            return;

        driver.SendDebugMessage(hasContact
            ? "[ShipShip] Hull contact started"
            : "[ShipShip] Hull contact ended");
    }

    private static void UpdateShipCornerMarkers(Slave ship)
    {
        var templateId = CornerMarkerTemplateId;
        if (templateId == 0 || ship.ParentWorld is null)
        {
            DespawnMarkers(_shipCornerMarkers, ship.Id);
            return;
        }

        var rb = ship.RigidBody;
        var model = ship.ShipController?.ShipModel;
        if (rb is null || model is null)
        {
            DespawnMarkers(_shipCornerMarkers, ship.Id);
            return;
        }

        var zoneId = ship.Transform.ZoneId;
        var set = _shipCornerMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(8));
        if (set.ZoneId != zoneId || set.TemplateId != templateId)
        {
            DespawnMarkers(_shipCornerMarkers, ship.Id);
            set = _shipCornerMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(8));
            set.ZoneId = zoneId;
            set.TemplateId = templateId;
        }

        // REAL physics box corners, but expressed in GAME coordinates with the same quaternion component mapping
        // used by PhysicsManager.SyncTransformWithRigidBody (rotation.X, rotation.Z, rotation.Y, rotation.W).
        //
        // Reason: a pure axis permutation (phys -> game) is an odd permutation (changes handedness),
        // so "rotate in phys then permute" can appear to flip left/right in game space.
        // Computing directly in game space with the mapped quaternion keeps turn direction consistent.
        //
        // ShipController.Build():
        // - BoxShape(MassBoxSizeX, MassBoxSizeZ, MassBoxSizeY)
        // - TransformedShape offset = (MassCenterX, MassCenterZ, MassCenterY)
        var scale = MathF.Max(0.01f, ship.Scale);
        var hx = model.MassBoxSizeX * scale * 0.5f;
        var hy = ShipController.ShipMassBoxDefaults.GetSizeZ(model.MassBoxSizeZ) * scale * 0.5f;
        var hz = model.MassBoxSizeY * scale * 0.5f;

        // See UpdateShipAxisMarkers comment above.
        var rotGame = GetTransformRotationMatrix(ship.Transform.Local.Rotation);

        var posGame0 = PhysToGame(rb.Position);
        var offsetLocalPhys = new JVector(
            model.MassCenterX * scale,
            ShipController.ShipMassBoxDefaults.GetCenterZ(model.MassCenterZ, model.MassBoxSizeZ) * scale,
            model.MassCenterY * scale);
        var offsetLocalGame = PhysVecToGame(offsetLocalPhys);
        var centerGame = posGame0 + Vector3.TransformNormal(offsetLocalGame, rotGame);

        Span<Vector3> localCornersGame =
        [
            PhysVecToGame(new JVector(+hx, +hy, +hz)),
            PhysVecToGame(new JVector(+hx, +hy, -hz)),
            PhysVecToGame(new JVector(+hx, -hy, +hz)),
            PhysVecToGame(new JVector(+hx, -hy, -hz)),
            PhysVecToGame(new JVector(-hx, +hy, +hz)),
            PhysVecToGame(new JVector(-hx, +hy, -hz)),
            PhysVecToGame(new JVector(-hx, -hy, +hz)),
            PhysVecToGame(new JVector(-hx, -hy, -hz)),
        ];

        for (var i = 0; i < 8; i++)
        {
            var posGame = centerGame + Vector3.TransformNormal(localCornersGame[i], rotGame);
            UpdateMarker(ship.ParentWorld, set, i, templateId, zoneId, posGame);
        }
    }

    /// <summary>
    /// EN: Called from ship↔ship resolver when overlap response happened.
    /// RU: Вызывается из ship↔ship резолвера при наличии overlap/реакции.
    /// </summary>
    public static void OnResolvedShipPair(Slave a, Slave b, float penetrationMeters, float nx, float nz, float impactSpeedMps)
    {
        var anyShipShipDebug = Enabled || ShipShipContactLatchChatEnabled || ShipShipResolveDetailChatEnabled;
        if (!anyShipShipDebug)
            return;

        // Extend "contact active" window; TickShip will emit start/end messages when Enabled.
        var nowMs = Environment.TickCount64;
        const int holdMs = 800;
        _shipContactUntilMs[a.Id] = nowMs + holdMs;
        _shipContactUntilMs[b.Id] = nowMs + holdMs;

        if (!ShipShipResolveDetailChatEnabled)
            return;

        SendShipShipDetail(a, b, penetrationMeters, nx, nz, impactSpeedMps);
        SendShipShipDetail(b, a, penetrationMeters, -nx, -nz, impactSpeedMps);
    }

    private static void SendShipShipDetail(Slave self, Slave other, float pen, float nx, float nz, float impactSpeedMps)
    {
        var driver = TryGetDriver(self);
        if (driver is null || !CanReceive(driver))
            return;

        var nowMs = Environment.TickCount64;
        if (ThrottleMsPerShip > 0)
        {
            var last = _lastDetailMsgAtMs.GetOrAdd(self.Id, 0);
            if (nowMs - last < ThrottleMsPerShip)
                return;
            _lastDetailMsgAtMs[self.Id] = nowMs;
        }

        driver.SendDebugMessage(
            $"[ShipShip] ship={self.ObjId} pair={other.ObjId} pen={pen:F3}m v={impactSpeedMps:F2}m/s n=({nx:F2},{nz:F2})");
    }

    /// <summary>
    /// EN: Called when shore latch flips. Sends chat messages "collided with ground" / "back in water".
    /// RU: Вызывается при смене shore latch. Пишет в чат "collided with ground" / "back in water".
    /// </summary>
    public static void OnShoreLatchChanged(Slave ship, bool latched)
    {
        if (!ShoreLatchChatEnabled)
            return;

        var driver = TryGetDriver(ship);
        if (driver is null || !CanReceive(driver))
            return;

        driver.SendDebugMessage(latched
            ? "[ShipShore] Ship collided with ground"
            : "[ShipShore] Ship is back in water");
    }

    /// <summary>
    /// EN: Feed shore probe/contact points for marker visualization.
    /// RU: Передать точки shore-проб/контакта для визуализации маркерами.
    /// </summary>
    public static void OnShoreProbe(
        Slave ship,
        float probeX, float probeY, float floorZ,
        float cliffX, float cliffY, float cliffZ,
        float boatCenterX, float boatCenterY, float boatBottomZ,
        float waterSurfaceZ,
        float penetrationMeters)
    {
        if (ship is null)
            return;

        if (!Enabled)
            DespawnMarkers(_shoreMarkers, ship.Id);
        else
        {
            var templateId = ShoreMarkerTemplateId;
            if (templateId == 0 || ship.ParentWorld is null || ship.Transform is null)
                DespawnMarkers(_shoreMarkers, ship.Id);
            else
            {
                var zoneId = ship.Transform.ZoneId;
                var set = _shoreMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(4));
                if (set.ZoneId != zoneId || set.TemplateId != templateId)
                {
                    DespawnMarkers(_shoreMarkers, ship.Id);
                    set = _shoreMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(4));
                    set.ZoneId = zoneId;
                    set.TemplateId = templateId;
                }

                UpdateMarker(ship.ParentWorld, set, 0, templateId, zoneId, new Vector3(probeX, probeY, floorZ));
                UpdateMarker(ship.ParentWorld, set, 1, templateId, zoneId, new Vector3(cliffX, cliffY, cliffZ));
                UpdateMarker(ship.ParentWorld, set, 2, templateId, zoneId, new Vector3(boatCenterX, boatCenterY, boatBottomZ));
                UpdateMarker(ship.ParentWorld, set, 3, templateId, zoneId, new Vector3(probeX, probeY, waterSurfaceZ));
            }
        }

        if (penetrationMeters <= 0.0f || !ShorePenetrationChatEnabled)
            return;

        var driver = TryGetDriver(ship);
        if (driver is null || !CanReceive(driver))
            return;

        var nowMs = Environment.TickCount64;
        if (ThrottleMsPerShip > 0)
        {
            var last = _lastShorePenMsgAtMs.GetOrAdd(ship.Id, 0);
            if (nowMs - last < ThrottleMsPerShip)
                return;
            _lastShorePenMsgAtMs[ship.Id] = nowMs;
        }

        driver.SendDebugMessage($"[ShipShore] ship={ship.ObjId} pen={penetrationMeters:F3}m");
    }

    private static void UpdateMarker(AAEmu.Game.Models.Game.World.WorldInstance world, MarkerSet set, int idx, uint templateId, uint zoneId, Vector3 pos)
    {
        var g = set.Markers[idx];
        if (g is null || g.ParentWorld != world)
        {
            var spawner = new GimmickSpawner(world)
            {
                UnitId = templateId,
                RespawnTime = 0,
                Scale = MarkerScale,
                Position = new WorldSpawnPosition
                {
                    ZoneId = zoneId,
                    X = pos.X,
                    Y = pos.Y,
                    Z = pos.Z,
                    Roll = 0f,
                    Pitch = 0f,
                    Yaw = 0f
                }
            };

            var spawned = spawner.Spawn(0);
            if (spawned is null)
                return;

            spawned.SetScale(MarkerScale);
            set.Spawners[idx] = spawner;
            set.Markers[idx] = spawned;
            g = spawned;
        }

        g.Transform.Local.SetPosition(pos.X, pos.Y, pos.Z);
        g.Transform.FinalizeTransform();
        g.Vel = Vector3.Zero;
        g.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;
        g.BroadcastPacket(new SCGimmickMovementPacket(g), false);
    }

    private static void DespawnAll(uint shipId)
    {
        DespawnMarkers(_shipCornerMarkers, shipId);
        DespawnMarkers(_shipAxisLenMarkers, shipId);
        DespawnMarkers(_shipAxisBeamMarkers, shipId);
        DespawnMarkers(_shipAxisUpMarkers, shipId);
        DespawnMarkers(_shoreMarkers, shipId);
        _shipContactUntilMs.TryRemove(shipId, out _);
        _shipContactLatched.TryRemove(shipId, out _);
        _lastDetailMsgAtMs.TryRemove(shipId, out _);
        _lastAxesMsgAtMs.TryRemove(shipId, out _);
        _lastShorePenMsgAtMs.TryRemove(shipId, out _);
        _lastSpeedMsgAtMs.TryRemove(shipId, out _);
    }

    private static void DespawnMarkers(ConcurrentDictionary<uint, MarkerSet> dict, uint shipId)
    {
        if (!dict.TryRemove(shipId, out var set))
            return;
        for (var i = 0; i < set.Markers.Length; i++)
        {
            try
            {
                var g = set.Markers[i];
                var sp = set.Spawners[i];
                if (g is null || sp is null)
                    continue;
                sp.Despawn(g);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static Character? TryGetDriver(Slave s)
    {
        return s.AttachedCharacters.TryGetValue(AttachPointKind.Driver, out var driver) ? driver : null;
    }

    private static bool CanReceive(Character c)
    {
        return CharacterManager.Instance.GetEffectiveAccessLevel(c) >= MinAccessLevel;
    }

    private static Vector3 PhysToGame(JVector phys)
    {
        // Game coords: (X,Y,Z) == (phys X, phys Z, phys Y)
        return new Vector3(phys.X, phys.Z, phys.Y);
    }

    private static Vector3 PhysVecToGame(JVector v)
    {
        // Same axis mapping as PhysToGame but for a vector (no translation).
        return new Vector3(v.X, v.Z, v.Y);
    }

    private static Matrix4x4 GetTransformRotationMatrix(Vector3 rpy)
    {
        // Use the project's own Euler->Quaternion conversion.
        // PositionAndRotation stores Euler as (roll=X, pitch=Y, yaw=Z) in radians,
        // with yaw around +Z (up). This matches how Transform is intended to behave.
        var q = Quaternion.Normalize(PositionAndRotation.ToQuaternion(rpy));
        return Matrix4x4.CreateFromQuaternion(q);
    }
}

