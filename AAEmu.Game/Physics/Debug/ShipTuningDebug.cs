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
    private const int MinChatThrottleIntervalMsFloor = 250;
    private const int ShipPairContactHoldMs = 800;
    private const int MassBoxCornerCount = 8;

    private static class ChatTags
    {
        public const string ShipSpeed = "[ShipSpeed]";
        public const string ShipBox = "[ShipBox]";
        public const string ShipShip = "[ShipShip]";
        public const string ShipShore = "[ShipShore]";
    }

    #region Public debug switches

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

    #endregion

    #region Throttle & marker state

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

    #endregion

    #region Mass-box geometry (game space)

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

    /// <summary>Half-extents (phys local X,Y,Z), box center and rotation in game space — same basis as <see cref="ShipController.Build"/>.</summary>
    private readonly record struct MassBoxGameGeometry(float Hx, float Hy, float Hz, Vector3 CenterGame, Matrix4x4 RotGame);

    private static bool TryGetMassBoxGameGeometry(Slave ship, out MassBoxGameGeometry geo)
    {
        geo = default;
        var rb = ship.RigidBody;
        var model = ship.ShipController?.ShipModel;
        if (rb is null || model is null || ship.Transform is null)
            return false;

        var scale = MathF.Max(0.01f, ship.Scale);
        var hx = model.MassBoxSizeX * scale * 0.5f;
        var hy = ShipController.ShipMassBoxDefaults.GetSizeZ(model.MassBoxSizeZ) * scale * 0.5f;
        var hz = model.MassBoxSizeY * scale * 0.5f;
        var rotGame = GetTransformRotationMatrix(ship.Transform.Local.Rotation);
        var posGame0 = PhysToGame(rb.Position);
        var offsetLocalPhys = new JVector(
            model.MassCenterX * scale,
            ShipController.ShipMassBoxDefaults.GetCenterZ(model.MassCenterZ, model.MassBoxSizeZ) * scale,
            model.MassCenterY * scale);
        var offsetLocalGame = PhysVecToGame(offsetLocalPhys);
        var centerGame = posGame0 + Vector3.TransformNormal(offsetLocalGame, rotGame);
        geo = new MassBoxGameGeometry(hx, hy, hz, centerGame, rotGame);
        return true;
    }

    private static void FillMassBoxLocalCornersGame(float hx, float hy, float hz, Span<Vector3> corners)
    {
        corners[0] = PhysVecToGame(new JVector(+hx, +hy, +hz));
        corners[1] = PhysVecToGame(new JVector(+hx, +hy, -hz));
        corners[2] = PhysVecToGame(new JVector(+hx, -hy, +hz));
        corners[3] = PhysVecToGame(new JVector(+hx, -hy, -hz));
        corners[4] = PhysVecToGame(new JVector(-hx, +hy, +hz));
        corners[5] = PhysVecToGame(new JVector(-hx, +hy, -hz));
        corners[6] = PhysVecToGame(new JVector(-hx, -hy, +hz));
        corners[7] = PhysVecToGame(new JVector(-hx, -hy, -hz));
    }

    private static void GetMassBoxWorldVerticalExtent(in MassBoxGameGeometry g, Span<Vector3> cornerScratch, out float minZ, out float maxZ)
    {
        FillMassBoxLocalCornersGame(g.Hx, g.Hy, g.Hz, cornerScratch);
        minZ = float.PositiveInfinity;
        maxZ = float.NegativeInfinity;
        for (var i = 0; i < MassBoxCornerCount; i++)
        {
            var w = g.CenterGame + Vector3.TransformNormal(cornerScratch[i], g.RotGame);
            if (w.Z < minZ) minZ = w.Z;
            if (w.Z > maxZ) maxZ = w.Z;
        }
    }

    private static void DespawnAllAxisMarkerSets(uint shipId)
    {
        DespawnMarkers(_shipAxisLenMarkers, shipId);
        DespawnMarkers(_shipAxisBeamMarkers, shipId);
        DespawnMarkers(_shipAxisUpMarkers, shipId);
    }

    #endregion

    #region Marker & chat helpers

    /// <summary>
    /// One gimmick per ship for an axis; despawn when <paramref name="templateId"/> is 0.
    /// </summary>
    private static MarkerSet? EnsureSingleAxisMarkerSet(
        ConcurrentDictionary<uint, MarkerSet> dict,
        uint shipId,
        uint zoneId,
        uint templateId)
    {
        if (templateId == 0)
        {
            DespawnMarkers(dict, shipId);
            return null;
        }

        var set = dict.GetOrAdd(shipId, _ => new MarkerSet(1));
        if (set.ZoneId != zoneId || set.TemplateId != templateId)
        {
            DespawnMarkers(dict, shipId);
            set = dict.GetOrAdd(shipId, _ => new MarkerSet(1));
            set.ZoneId = zoneId;
            set.TemplateId = templateId;
        }

        return set;
    }

    /// <returns><see langword="false"/> if this ship is still inside the throttle window.</returns>
    private static bool TryConsumeDebugChatThrottle(ConcurrentDictionary<uint, long> lastByShip, uint shipId, int intervalMs)
    {
        if (intervalMs <= 0)
            return true;

        var nowMs = Environment.TickCount64;
        var last = lastByShip.GetOrAdd(shipId, 0);
        if (nowMs - last < intervalMs)
            return false;

        lastByShip[shipId] = nowMs;
        return true;
    }

    #endregion

    #region Per-tick & markers

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

        var intervalMs = Math.Max(MinChatThrottleIntervalMsFloor, ThrottleMsPerShip);
        if (!TryConsumeDebugChatThrottle(_lastSpeedMsgAtMs, ship.Id, intervalMs))
            return;

        var isGrounded = (ship.CachedFloorLevel > ship.CachedWaterSurface) || ship.GroundContactLatched;
        var escapeThrottleSign = ship.GroundedByStern ? 1 : -1;
        var isEscapeInputOnGround = isGrounded && ship.ThrottleRequest != 0 && Math.Sign(ship.ThrottleRequest) == escapeThrottleSign;

        var cap = ShipController.ShipMotionDefaults.GroundEscapeMaxSpeedAbs;

        driver.SendDebugMessage(
            $"{ChatTags.ShipSpeed} ship={ship.ObjId} v={ship.Speed:F2} grounded={isGrounded} latched={ship.GroundContactLatched} byStern={ship.GroundedByStern} thrReq={ship.ThrottleRequest} escape={isEscapeInputOnGround} escapeCap={cap:F2}");
    }

    private static void UpdateShipAxisMarkers(Slave ship)
    {
        if (!AxisMarkersEnabled)
        {
            DespawnAllAxisMarkerSets(ship.Id);
            return;
        }

        var lenTemplateId = AxisLengthMarkerTemplateId;
        var beamTemplateId = AxisBeamMarkerTemplateId;
        var upTemplateId = AxisUpMarkerTemplateId;
        if ((lenTemplateId == 0 && beamTemplateId == 0 && upTemplateId == 0) || ship.ParentWorld is null)
        {
            DespawnAllAxisMarkerSets(ship.Id);
            return;
        }

        var model = ship.ShipController?.ShipModel;
        if (model is null || !TryGetMassBoxGameGeometry(ship, out var geo))
        {
            DespawnAllAxisMarkerSets(ship.Id);
            return;
        }

        var zoneId = ship.Transform.ZoneId;
        var setLen = EnsureSingleAxisMarkerSet(_shipAxisLenMarkers, ship.Id, zoneId, lenTemplateId);
        var setBeam = EnsureSingleAxisMarkerSet(_shipAxisBeamMarkers, ship.Id, zoneId, beamTemplateId);
        var setUp = EnsureSingleAxisMarkerSet(_shipAxisUpMarkers, ship.Id, zoneId, upTemplateId);

        var extra = MathF.Max(0f, AxisMarkerExtraMeters);
        var hzE = geo.Hz + extra;
        var hxE = geo.Hx + extra;
        var hyE = geo.Hy + extra;

        // +Length / +Beam / +Up: local +Z / +X / +Y of physics box (see ShipController.Build).
        var posLen = geo.CenterGame + Vector3.TransformNormal(PhysVecToGame(new JVector(0f, 0f, hzE)), geo.RotGame);
        var posBeam = geo.CenterGame + Vector3.TransformNormal(PhysVecToGame(new JVector(hxE, 0f, 0f)), geo.RotGame);
        var posUp = geo.CenterGame + Vector3.TransformNormal(PhysVecToGame(new JVector(0f, hyE, 0f)), geo.RotGame);

        Span<Vector3> cornerScratch = stackalloc Vector3[MassBoxCornerCount];
        GetMassBoxWorldVerticalExtent(in geo, cornerScratch, out var minZ, out var maxZ);

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

        var intervalMs = Math.Max(MinChatThrottleIntervalMsFloor, ThrottleMsPerShip);
        if (!TryConsumeDebugChatThrottle(_lastAxesMsgAtMs, ship.Id, intervalMs))
            return;

        var waterZ = ship.CachedWaterSurface;
        driver.SendDebugMessage(
            $"{ChatTags.ShipBox} ship={ship.ObjId} sizeXYZ=({model.MassBoxSizeX:F2},{model.MassBoxSizeY:F2},{model.MassBoxSizeZ:F2}) centerXYZ=({model.MassCenterX:F2},{model.MassCenterY:F2},{model.MassCenterZ:F2}) z=[{minZ:F2}..{maxZ:F2}] waterZ={waterZ:F2}");
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
            ? $"{ChatTags.ShipShip} Hull contact started"
            : $"{ChatTags.ShipShip} Hull contact ended");
    }

    private static void UpdateShipCornerMarkers(Slave ship)
    {
        var templateId = CornerMarkerTemplateId;
        if (templateId == 0 || ship.ParentWorld is null)
        {
            DespawnMarkers(_shipCornerMarkers, ship.Id);
            return;
        }

        if (!TryGetMassBoxGameGeometry(ship, out var geo))
        {
            DespawnMarkers(_shipCornerMarkers, ship.Id);
            return;
        }

        var zoneId = ship.Transform!.ZoneId;
        var set = _shipCornerMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(MassBoxCornerCount));
        if (set.ZoneId != zoneId || set.TemplateId != templateId)
        {
            DespawnMarkers(_shipCornerMarkers, ship.Id);
            set = _shipCornerMarkers.GetOrAdd(ship.Id, _ => new MarkerSet(MassBoxCornerCount));
            set.ZoneId = zoneId;
            set.TemplateId = templateId;
        }

        Span<Vector3> localCorners = stackalloc Vector3[MassBoxCornerCount];
        FillMassBoxLocalCornersGame(geo.Hx, geo.Hy, geo.Hz, localCorners);
        for (var i = 0; i < MassBoxCornerCount; i++)
        {
            var posGame = geo.CenterGame + Vector3.TransformNormal(localCorners[i], geo.RotGame);
            UpdateMarker(ship.ParentWorld, set, i, templateId, zoneId, posGame);
        }
    }

    #endregion

    #region Ship↔ship & shore callbacks

    /// <summary>
    /// EN: Called from ship↔ship resolver when overlap response happened.
    /// RU: Вызывается из ship↔ship резолвера при наличии overlap/реакции.
    /// </summary>
    public static void OnResolvedShipPair(Slave a, Slave b, float penetrationMeters, float nx, float nz, float impactSpeedMps)
    {
        var shipPairDebugActive = Enabled || ShipShipContactLatchChatEnabled || ShipShipResolveDetailChatEnabled;
        if (!shipPairDebugActive)
            return;

        // Extend "contact active" window; TickShip will emit start/end messages when Enabled.
        var nowMs = Environment.TickCount64;
        var holdUntil = nowMs + ShipPairContactHoldMs;
        _shipContactUntilMs[a.Id] = holdUntil;
        _shipContactUntilMs[b.Id] = holdUntil;

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

        if (!TryConsumeDebugChatThrottle(_lastDetailMsgAtMs, self.Id, ThrottleMsPerShip))
            return;

        driver.SendDebugMessage(
            $"{ChatTags.ShipShip} ship={self.ObjId} pair={other.ObjId} pen={pen:F3}m v={impactSpeedMps:F2}m/s n=({nx:F2},{nz:F2})");
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
            ? $"{ChatTags.ShipShore} Ship collided with ground"
            : $"{ChatTags.ShipShore} Ship is back in water");
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

        if (!TryConsumeDebugChatThrottle(_lastShorePenMsgAtMs, ship.Id, ThrottleMsPerShip))
            return;

        driver.SendDebugMessage($"{ChatTags.ShipShore} ship={ship.ObjId} pen={penetrationMeters:F3}m");
    }

    #endregion

    #region Marker spawn & lifecycle

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

    #endregion

    #region Driver & coordinates

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

    #endregion
}

