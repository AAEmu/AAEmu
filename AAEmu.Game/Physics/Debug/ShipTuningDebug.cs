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
/// EN: Dev-only ship physics tuning & debug: ship↔ship, ship↔shore, plus ShipController tuning.
/// RU: Dev-only тюнинг и дебаг корабельной физики: ship↔ship, ship↔shore, а также тюнинг ShipController.
/// EN: Gimmick markers use template ids from table <c>gimmicks</c> — see <see cref="CornerMarkerTemplateId"/>, <see cref="ShoreMarkerTemplateId"/>, axis ids; set id to <c>0</c> or use flags (<see cref="Enabled"/>, <see cref="AxisMarkersEnabled"/>) to hide.
/// RU: Маркеры-гиммики — template id из таблицы <c>gimmicks</c> (см. углы, берег, оси); выключить — id <c>0</c> или флаги (<see cref="Enabled"/>, <see cref="AxisMarkersEnabled"/>).
/// </summary>
public static class ShipTuningDebug
{
    /// <summary>
    /// EN: Master switch (use Hot Reload; no GM commands). When false: all ship debug gimmicks despawn and runtime tuning overrides fall back to defaults in interaction classes — not only visuals.
    /// RU: Главный переключатель (Hot Reload; без GM). Если false — снимаются все дебаг-маркеры кораблей и runtime-тюнинг из этого класса не применяется (подставляются дефолты в классах взаимодействий), не только картинка.
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
    /// EN: Shore probe/contact markers (table <c>gimmicks</c>). Default id <c>28</c>. To disable only shore gimmicks: return <c>0</c>. Ship physics uses <see cref="ShipShoreInteraction.ShorePhysicsDefaults"/> when <see cref="Enabled"/> is false.
    /// RU: Маркеры берега/проб (таблица <c>gimmicks</c>). По умолчанию id <c>28</c>. Выключить только маркеры берега: <c>return 0</c>. Физика берега — дефолты в <see cref="ShipShoreInteraction.ShorePhysicsDefaults"/>, если <see cref="Enabled"/> false.
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
    /// EN: If true, draw markers only for ships with a driver.
    /// RU: Если true — рисовать маркеры только для кораблей с водителем.
    /// </summary>
    public static bool DrawOnlyWhenDriven => GetDrawOnlyWhenDriven();
    private static bool GetDrawOnlyWhenDriven() => false;

    /// <summary>
    /// EN: Reserved (not used for physics; shore behavior is gated by <see cref="Enabled"/> + <see cref="ShipShoreInteraction.ShorePhysicsDefaults"/>).
    /// RU: Зарезервировано (физика берега завязана на <see cref="Enabled"/> и <see cref="ShipShoreInteraction.ShorePhysicsDefaults"/>).
    /// </summary>
    public static bool ShoreEnabled => GetShoreEnabled();
    private static bool GetShoreEnabled() => false;

    /// <summary>
    /// EN: Gimmicks for +Length, +Beam, +Up axis (three markers). When false, all three are despawned. Per-axis off: set that axis template id to 0.
    /// RU: Гиммики для осей +Length, +Beam, +Up (три маркера). Если false — снимаются все три. Отдельную ось: template id этой оси = 0.
    /// </summary>
    public static bool AxisMarkersEnabled => GetAxisMarkersEnabled();
    private static bool GetAxisMarkersEnabled() => false;

    /// <summary>
    /// EN: Extra distance (meters) beyond half-extent for axis markers.
    /// RU: Доп. расстояние (метры) за пределы half-extent для осевых маркеров.
    /// </summary>
    public static float AxisMarkerExtraMeters => GetAxisMarkerExtraMeters();
    private static float GetAxisMarkerExtraMeters() => 1.25f;

    /// <summary>
    /// EN: Mass-box tuning (center/size overrides). Useful when the hull OBB is too low/high vs the visible hull.
    /// RU: Тюнинг mass-box (переопределения центра/размера). Полезно, когда OBB корпуса слишком низко/высоко относительно модели.
    /// </summary>
    public static class HullBoxTuning
    {
        /// <summary>
        /// EN: Additive vertical offset to MassCenterZ (meters). Positive lifts the box up.
        /// RU: Аддитивный вертикальный сдвиг к MassCenterZ (метры). Положительное значение поднимает бокс вверх.
        /// </summary>
        public static float CenterZAddMeters => GetCenterZAddMeters();
        private static float GetCenterZAddMeters() => ShipController.ShipMassBoxDefaults.CenterZAddMeters;

        /// <summary>
        /// EN: Additive vertical offset to MassCenterZ as a fraction of MassBoxSizeZ. Example: 0.30 lifts the box by 30% of its height.
        /// RU: Аддитивный вертикальный сдвиг к MassCenterZ как доля MassBoxSizeZ. Например: 0.30 поднимает бокс на 30% его высоты.
        /// </summary>
        public static float CenterZAddFracOfSizeZ => GetCenterZAddFracOfSizeZ();
        private static float GetCenterZAddFracOfSizeZ() => ShipController.ShipMassBoxDefaults.CenterZAddFracOfSizeZ;

        /// <summary>
        /// EN: Multiply MassBoxSizeZ (height). 1 = no change.
        /// RU: Множитель MassBoxSizeZ (высота). 1 = без изменений.
        /// </summary>
        public static float SizeZMul => GetSizeZMul();
        private static float GetSizeZMul() => ShipController.ShipMassBoxDefaults.SizeZMul;

        /// <summary>
        /// EN: Additive adjustment to MassBoxSizeZ (meters). Applied after SizeZMul.
        /// RU: Аддитивная поправка к MassBoxSizeZ (метры). Применяется после SizeZMul.
        /// </summary>
        public static float SizeZAddMeters => GetSizeZAddMeters();
        private static float GetSizeZAddMeters() => ShipController.ShipMassBoxDefaults.SizeZAddMeters;

        internal static float GetCenterZ(float baseCenterZ, float baseSizeZ) =>
            baseCenterZ + baseSizeZ * CenterZAddFracOfSizeZ + CenterZAddMeters;

        internal static float GetSizeZ(float baseSizeZ)
        {
            var z = baseSizeZ * SizeZMul + SizeZAddMeters;
            return MathF.Max(0.01f, z);
        }
    }

    /// <summary>
    /// EN: Ship↔ship SAT/response tuning (runtime fields for Hot Reload).
    /// RU: Тюнинг ship↔ship SAT/реакции (runtime-поля для Hot Reload).
    /// </summary>
    public static class ShipShipTuning
    {
        /// <summary>
        /// EN: Half-length multiplier for SAT overlap test only (keep near 1).
        /// RU: Множитель полу-длины только для SAT-детекта (держать близко к 1).
        /// </summary>
        public static float HullDetectInflateLength => GetHullDetectInflateLength();
        private static float GetHullDetectInflateLength() => ShipShipInteraction.PhysicsDefaults.HullDetectInflateLength;

        /// <summary>
        /// EN: Half-beam multiplier for SAT overlap test only.
        /// RU: Множитель полу-ширины только для SAT-детекта.
        /// </summary>
        public static float HullDetectInflateBeam => GetHullDetectInflateBeam();
        private static float GetHullDetectInflateBeam() => ShipShipInteraction.PhysicsDefaults.HullDetectInflateBeam;

        /// <summary>
        /// EN: Extra tightening of beam in SAT only (reduces early side contact from oversized mass box).
        /// RU: Доп. ужатие ширины только в SAT (убирает ранний боковой контакт из-за широкого mass box).
        /// </summary>
        public static float BeamDetectTightenMul => GetBeamDetectTightenMul();
        private static float GetBeamDetectTightenMul() => ShipShipInteraction.PhysicsDefaults.BeamDetectTightenMul;

        /// <summary>
        /// EN: Ignore overlap response below this penetration depth (meters).
        /// RU: Игнорировать реакцию, если penetration меньше этого (метры).
        /// </summary>
        public static float MinPenetrationToAct => GetMinPenetrationToAct();
        private static float GetMinPenetrationToAct() => ShipShipInteraction.PhysicsDefaults.MinPenetrationToAct;

        /// <summary>
        /// EN: Ignore periodic hull-damage below this penetration depth (meters).
        /// RU: Не наносить периодический урон корпусу, если penetration меньше этого (метры).
        /// </summary>
        public static float MinPenetrationToDamage => GetMinPenetrationToDamage();
        private static float GetMinPenetrationToDamage() => ShipShipInteraction.PhysicsDefaults.MinPenetrationToDamage;

        /// <summary>
        /// EN: Ramp tangential slip damping over this depth range past MinPenetrationToAct (meters).
        /// RU: Наращивать демпф тангенциального скольжения на этом диапазоне глубины сверх MinPenetrationToAct (метры).
        /// </summary>
        public static float TangentialRampDepthMeters => GetTangentialRampDepthMeters();
        private static float GetTangentialRampDepthMeters() => ShipShipInteraction.PhysicsDefaults.TangentialRampDepthMeters;

        /// <summary>
        /// EN: Multiplier on positional separation push once overlap exists.
        /// RU: Множитель раздвижения (push) после обнаружения overlap.
        /// </summary>
        public static float SeparationPushMultiplier => GetSeparationPushMultiplier();
        private static float GetSeparationPushMultiplier() => ShipShipInteraction.PhysicsDefaults.SeparationPushMultiplier;

        /// <summary>
        /// EN: Extra separation slack added to computed overlap (meters).
        /// RU: Доп. зазор к раздвижению сверх overlap (метры).
        /// </summary>
        public static float SeparationSlackMeters => GetSeparationSlackMeters();
        private static float GetSeparationSlackMeters() => ShipShipInteraction.PhysicsDefaults.SeparationSlackMeters;

        /// <summary>
        /// EN: Fraction of relative closing speed along normal to remove (1 = full stop along normal).
        /// RU: Доля гашения относительной скорости вдоль нормали (1 = полностью убрать вдоль нормали).
        /// </summary>
        public static float ClosingSpeedDamp => GetClosingSpeedDamp();
        private static float GetClosingSpeedDamp() => ShipShipInteraction.PhysicsDefaults.ClosingSpeedDamp;

        /// <summary>
        /// EN: Tangential slip damping factor while overlapping (0..1).
        /// RU: Коэффициент демпфа тангенциального скольжения при overlap (0..1).
        /// </summary>
        public static float TangentialSlipDamp => GetTangentialSlipDamp();
        private static float GetTangentialSlipDamp() => ShipShipInteraction.PhysicsDefaults.TangentialSlipDamp;

        /// <summary>
        /// EN: Minimum vertical overlap (meters) to consider ships colliding.
        /// RU: Минимальный вертикальный overlap (метры) чтобы считать столкновение.
        /// </summary>
        public static float MinVerticalOverlap => GetMinVerticalOverlap();
        private static float GetMinVerticalOverlap() => ShipShipInteraction.PhysicsDefaults.MinVerticalOverlap;

        /// <summary>
        /// EN: Outer resolve passes per tick (CPU vs stability).
        /// RU: Внешние проходы резолва за тик (CPU vs стабильность).
        /// </summary>
        public static int ResolvePasses => GetResolvePasses();
        private static int GetResolvePasses() => ShipShipInteraction.PhysicsDefaults.ResolvePasses;

        /// <summary>
        /// EN: Max depenetration iterations per pair per pass.
        /// RU: Макс. итераций раздвижения на пару за проход.
        /// </summary>
        public static int MaxPairIterations => GetMaxPairIterations();
        private static int GetMaxPairIterations() => ShipShipInteraction.PhysicsDefaults.MaxPairIterations;

        /// <summary>
        /// EN: Penetration depth where “deep penetration” push boost starts (meters).
        /// RU: Глубина penetration, с которой начинается усиление push (метры).
        /// </summary>
        public static float DeepPenetrationStart => GetDeepPenetrationStart();
        private static float GetDeepPenetrationStart() => ShipShipInteraction.PhysicsDefaults.DeepPenetrationStart;

        /// <summary>
        /// EN: Boost factor applied as penetration exceeds DeepPenetrationStart.
        /// RU: Коэффициент усиления push при penetration больше DeepPenetrationStart.
        /// </summary>
        public static float DeepPenetrationBoost => GetDeepPenetrationBoost();
        private static float GetDeepPenetrationBoost() => ShipShipInteraction.PhysicsDefaults.DeepPenetrationBoost;

        /// <summary>
        /// EN: Floor on half-separation distance (meters).
        /// RU: Минимальная полу-дистанция раздвижения (метры).
        /// </summary>
        public static float MinHalfSeparationMeters => GetMinHalfSeparationMeters();
        private static float GetMinHalfSeparationMeters() => ShipShipInteraction.PhysicsDefaults.MinHalfSeparationMeters;

        /// <summary>
        /// EN: Stop iterating if computed separation is below this (meters) to avoid micro-jitter.
        /// RU: Остановить итерации, если раздвижение меньше этого (метры), чтобы убрать микродрожь.
        /// </summary>
        public static float MinLinearSeparationToApplyMeters => GetMinLinearSeparationToApplyMeters();
        private static float GetMinLinearSeparationToApplyMeters() => ShipShipInteraction.PhysicsDefaults.MinLinearSeparationToApplyMeters;

        /// <summary>
        /// EN: Cosine threshold for “nose cone” classification.
        /// RU: Порог косинуса для классификации “удар в нос”.
        /// </summary>
        public static float NoseContactCosThreshold => GetNoseContactCosThreshold();
        private static float GetNoseContactCosThreshold() => ShipShipInteraction.PhysicsDefaults.NoseContactCosThreshold;

        /// <summary>
        /// EN: Min interval between hull-collision damage ticks per other ship (seconds).
        /// RU: Минимальный интервал тиков урона от столкновения корпусом на конкретный другой корабль (сек).
        /// </summary>
        public static float HullCollisionDamageCooldownSec => GetHullCollisionDamageCooldownSec();
        private static float GetHullCollisionDamageCooldownSec() => ShipShipInteraction.PhysicsDefaults.HullCollisionDamageCooldownSec;

        /// <summary>
        /// EN: Relative speed threshold (m/s) where damage uses min % (non-nose).
        /// RU: Порог относительной скорости (м/с), ниже которого урон минимальный (не-носовые удары).
        /// </summary>
        public static float HullDamageLowSpeedThresholdMps => GetHullDamageLowSpeedThresholdMps();
        private static float GetHullDamageLowSpeedThresholdMps() => ShipShipInteraction.PhysicsDefaults.HullDamageLowSpeedThresholdMps;

        /// <summary>
        /// EN: Relative speed (m/s) where damage reaches max % (linear between thresholds).
        /// RU: Относительная скорость (м/с), при которой урон достигает максимума (линейно между порогами).
        /// </summary>
        public static float HullDamageInterpMaxMps => GetHullDamageInterpMaxMps();
        private static float GetHullDamageInterpMaxMps() => ShipShipInteraction.PhysicsDefaults.HullDamageSpeedInterpMaxMps;

        /// <summary>
        /// EN: Min % hull damage per tick.
        /// RU: Минимальный % урона корпуса за тик.
        /// </summary>
        public static int HullDamageSpeedScaledMinPercent => GetHullDamageSpeedScaledMinPercent();
        private static int GetHullDamageSpeedScaledMinPercent() => ShipShipInteraction.PhysicsDefaults.HullDamageSpeedScaledMinPercent;

        /// <summary>
        /// EN: Max % hull damage per tick.
        /// RU: Максимальный % урона корпуса за тик.
        /// </summary>
        public static int HullDamageSpeedScaledMaxPercent => GetHullDamageSpeedScaledMaxPercent();
        private static int GetHullDamageSpeedScaledMaxPercent() => ShipShipInteraction.PhysicsDefaults.HullDamageSpeedScaledMaxPercent;

        /// <summary>
        /// EN: How strongly mass differences steer who gets pushed: 0 = 50/50, 1 = inverse-mass (physical), &gt;1 = exaggerate (lighter ship moves more; values clamped).
        /// RU: Насколько масса влияет на раздвижение: 0 = поровну, 1 = обратно массе (как в физике), &gt;1 — усилить контраст (лёгкий сильнее отталкивается; края подрезаются).
        /// </summary>
        public static float MassPushStrength => GetMassPushStrength();
        private static float GetMassPushStrength() => ShipShipInteraction.PhysicsDefaults.MassPushStrength;
    }

    /// <summary>
    /// EN: Ship↔shore tuning mirrors ShipShoreInteraction constants (runtime fields for Hot Reload).
    /// RU: Тюнинг ship↔shore зеркалит константы ShipShoreInteraction (runtime-поля для Hot Reload).
    /// </summary>
    public static class ShoreTuning
    {
        /// <summary>
        /// EN: Effective "boat bottom" offset as a fraction of MassBoxSizeZ (height).
        /// EN: boatBottomY = rigidBodyY - MassBoxSizeZ*scale*frac. ~0.5 = keel near half-box below COM (matches legacy fallback).
        /// EN: Values &lt; ~0.4 lift the effective bottom (less penetration, more air gap); &gt; ~0.55 can over-embed.
        /// RU: Смещение "дна" как доля MassBoxSizeZ (высота).
        /// RU: boatBottomY = rigidBodyY - MassBoxSizeZ*scale*frac. ~0.5 — киль около половины высоты под COM (как legacy 0.5).
        /// RU: Меньше ~0.4 — «дно» выше, меньше penetration и больше визуальный зазор; больше ~0.55 — риск глубокого вдавливания.
        /// </summary>
        public static float BoatBottomOffsetFracOfSizeZ => GetBoatBottomOffsetFracOfSizeZ();
        private static float GetBoatBottomOffsetFracOfSizeZ() => ShipShoreInteraction.ShorePhysicsDefaults.BoatBottomOffsetFracOfSizeZ;

        /// <summary>
        /// EN: Ground friction multiplier on dry ground.
        /// RU: Трение на суше.
        /// </summary>
        public static float GroundFriction => GetGroundFriction();
        private static float GetGroundFriction() => ShipShoreInteraction.ShorePhysicsDefaults.GroundFriction;

        /// <summary>
        /// EN: Velocity/angular damping on dry ground.
        /// RU: Демпф скорости/угловой скорости на суше.
        /// </summary>
        public static float DryGroundCollisionDamping => GetDryGroundCollisionDamping();
        private static float GetDryGroundCollisionDamping() => ShipShoreInteraction.ShorePhysicsDefaults.DryGroundCollisionDamping;

        /// <summary>
        /// EN: Roll correction dead-zone (radians).
        /// RU: Мёртвая зона коррекции roll (радианы).
        /// </summary>
        public static float DryGroundRollDeadZoneRad => GetDryGroundRollDeadZoneRad();
        private static float GetDryGroundRollDeadZoneRad() => ShipShoreInteraction.ShorePhysicsDefaults.DryGroundRollDeadZoneRad;

        /// <summary>
        /// EN: Roll correction torque factor (tuning).
        /// RU: Коэффициент коррекции roll (тюнинг).
        /// </summary>
        public static float DryGroundRollTorqueMul => GetDryGroundRollTorqueMul();
        private static float GetDryGroundRollTorqueMul() => ShipShoreInteraction.ShorePhysicsDefaults.DryGroundRollTorqueMul;

        /// <summary>
        /// EN: Bow probe distance multiplier vs MassBoxSizeY (hull length).
        /// RU: Множитель дистанции носовой пробы от MassBoxSizeY (длина корпуса).
        /// </summary>
        public static float BowProbeMul => GetBowProbeMul();
        private static float GetBowProbeMul() => ShipShoreInteraction.ShorePhysicsDefaults.BowProbeMul;

        /// <summary>
        /// EN: Stern probe distance multiplier vs MassBoxSizeY (hull length).
        /// RU: Множитель дистанции кормовой пробы от MassBoxSizeY (длина корпуса).
        /// </summary>
        public static float SternProbeMul => GetSternProbeMul();
        private static float GetSternProbeMul() => ShipShoreInteraction.ShorePhysicsDefaults.SternProbeMul;

        /// <summary>
        /// EN: Cliff probe distance multiplier vs MassBoxSizeY (hull length).
        /// RU: Множитель дистанции пробы “обрыва/стены” от MassBoxSizeY (длина корпуса).
        /// </summary>
        public static float CliffProbeMul => GetCliffProbeMul();
        private static float GetCliffProbeMul() => ShipShoreInteraction.ShorePhysicsDefaults.CliffProbeMul;

        /// <summary>
        /// EN: Wall/cliff look-ahead distance as a fraction of half-length (added beyond the box edge).
        /// EN: 0 = probe exactly at bow/stern box edge; 0.2 = +20% of half-length further.
        /// RU: Дистанция look-ahead для “стены/обрыва” как доля half-length (прибавляется за край бокса).
        /// RU: 0 = проба ровно на кромке бокса нос/корма; 0.2 = +20% half-length дальше.
        /// </summary>
        public static float CliffProbeLookAheadMulOfHalfLength => GetCliffProbeLookAheadMulOfHalfLength();
        private static float GetCliffProbeLookAheadMulOfHalfLength() => ShipShoreInteraction.ShorePhysicsDefaults.CliffProbeLookAheadMulOfHalfLength;

        /// <summary>
        /// EN: Minimum look-ahead (meters) added beyond the box edge for wall/cliff probe.
        /// RU: Минимальный look-ahead (метры) за край бокса для пробы “стены/обрыва”.
        /// </summary>
        public static float CliffProbeMinLookAheadMeters => GetCliffProbeMinLookAheadMeters();
        private static float GetCliffProbeMinLookAheadMeters() => ShipShoreInteraction.ShorePhysicsDefaults.CliffProbeMinLookAheadMeters;

        /// <summary>
        /// EN: Cliff slope threshold (Δh / dist).
        /// RU: Порог “крутизны” (Δh / dist).
        /// </summary>
        public static float CliffSlopeFracThreshold => GetCliffSlopeFracThreshold();
        private static float GetCliffSlopeFracThreshold() => ShipShoreInteraction.ShorePhysicsDefaults.CliffSlopeFracThreshold;

        /// <summary>
        /// EN: Cliff must be above water by this margin (meters).
        /// RU: “Обрыв” считается только если выше воды на этот запас (метры).
        /// </summary>
        public static float CliffAboveWaterMargin => GetCliffAboveWaterMargin();
        private static float GetCliffAboveWaterMargin() => ShipShoreInteraction.ShorePhysicsDefaults.CliffAboveWaterMargin;

        /// <summary>
        /// EN: Shore latch enter hysteresis (meters).
        /// RU: Гистерезис входа в latch (метры).
        /// </summary>
        public static float ShoreEnterHyst => GetShoreEnterHyst();
        private static float GetShoreEnterHyst() => ShipShoreInteraction.ShorePhysicsDefaults.ShoreEnterHyst;

        /// <summary>
        /// EN: Shore latch exit hysteresis (meters).
        /// RU: Гистерезис выхода из latch (метры).
        /// </summary>
        public static float ShoreExitHyst => GetShoreExitHyst();
        private static float GetShoreExitHyst() => ShipShoreInteraction.ShorePhysicsDefaults.ShoreExitHyst;

        /// <summary>
        /// EN: Floor height smoothing response (lambda).
        /// RU: Сглаживание высоты пола (lambda).
        /// </summary>
        public static float FloorSmoothResponse => GetFloorSmoothResponse();
        private static float GetFloorSmoothResponse() => ShipShoreInteraction.ShorePhysicsDefaults.FloorSmoothResponse;

        /// <summary>
        /// EN: Pre-shore damping band (meters).
        /// RU: Полоса “перед берегом” для демпфа (метры).
        /// </summary>
        public static float PreShoreBand => GetPreShoreBand();
        private static float GetPreShoreBand() => ShipShoreInteraction.ShorePhysicsDefaults.PreShoreBand;

        /// <summary>
        /// EN: Penetration epsilon (meters).
        /// RU: Эпсилон penetration (метры).
        /// </summary>
        public static float PenetrationEpsilon => GetPenetrationEpsilon();
        private static float GetPenetrationEpsilon() => ShipShoreInteraction.ShorePhysicsDefaults.PenetrationEpsilon;

        /// <summary>
        /// EN: Penetration response (lambda).
        /// RU: Скорость реакции на penetration (lambda).
        /// </summary>
        public static float PenetrationResponse => GetPenetrationResponse();
        private static float GetPenetrationResponse() => ShipShoreInteraction.ShorePhysicsDefaults.PenetrationResponse;

        /// <summary>
        /// EN: Max up-step early after latching (m/tick).
        /// RU: Макс. подъём на раннем этапе latch (м/тик).
        /// </summary>
        public static float MaxUpStepEarly => GetMaxUpStepEarly();
        private static float GetMaxUpStepEarly() => ShipShoreInteraction.ShorePhysicsDefaults.MaxUpStepEarly;

        /// <summary>
        /// EN: Max up-step later while latched (m/tick).
        /// RU: Макс. подъём после стабилизации latch (м/тик).
        /// </summary>
        public static float MaxUpStepLate => GetMaxUpStepLate();
        private static float GetMaxUpStepLate() => ShipShoreInteraction.ShorePhysicsDefaults.MaxUpStepLate;

        /// <summary>
        /// EN: Visual ground pitch max (degrees).
        /// RU: Макс. визуальный ground pitch (градусы).
        /// </summary>
        public static float VisualGroundPitchMaxDeg => GetVisualGroundPitchMaxDeg();
        private static float GetVisualGroundPitchMaxDeg() => ShipShoreInteraction.ShorePhysicsDefaults.VisualGroundPitchMaxDeg;

        /// <summary>
        /// EN: Visual ground pitch probe distance (meters).
        /// RU: Дистанция проб для визуального pitch (метры).
        /// </summary>
        public static float VisualGroundPitchProbeDistance => GetVisualGroundPitchProbeDistance();
        private static float GetVisualGroundPitchProbeDistance() => ShipShoreInteraction.ShorePhysicsDefaults.VisualGroundPitchProbeDistance;

        /// <summary>
        /// EN: Visual ground pitch response (lambda).
        /// RU: Скорость реакции визуального pitch (lambda).
        /// </summary>
        public static float VisualGroundPitchResponse => GetVisualGroundPitchResponse();
        private static float GetVisualGroundPitchResponse() => ShipShoreInteraction.ShorePhysicsDefaults.VisualGroundPitchResponse;

        /// <summary>
        /// EN: Visual pitch floor smoothing response (lambda).
        /// RU: Сглаживание высот для визуального pitch (lambda).
        /// </summary>
        public static float VisualPitchFloorSmoothResponse => GetVisualPitchFloorSmoothResponse();
        private static float GetVisualPitchFloorSmoothResponse() => ShipShoreInteraction.ShorePhysicsDefaults.VisualPitchFloorSmoothResponse;
    }

    /// <summary>
    /// EN: ShipController tuning (runtime fields for Hot Reload).
    /// RU: Тюнинг ShipController (runtime-поля для Hot Reload).
    /// </summary>
    public static class ShipControllerTuning
    {
        /// <summary>
        /// EN: Max allowed speed (abs) while grounded AND using the correct "escape" throttle direction.
        /// RU: Максимальная скорость (по модулю) на мели при правильном газе "на выезд".
        /// </summary>
        public static float GroundEscapeMaxSpeedAbs => GetGroundEscapeMaxSpeedAbs();
        private static float GetGroundEscapeMaxSpeedAbs() => ShipController.PhysicsDefaults.GroundEscapeMaxSpeedAbs;

        /// <summary>
        /// EN: On shoal ground, max reverse speed as percent of max reverse on water (same ship/wind basis). 100 = same as water cap.
        /// RU: На мели: макс. задняя скорость в процентах от макс. задней на воде (та же база корабля/ветра). 100 = как на воде.
        /// </summary>
        public static float GroundReverseSpeedCapPercentOfWater => GetGroundReverseSpeedCapPercentOfWater();
        private static float GetGroundReverseSpeedCapPercentOfWater() => ShipController.PhysicsDefaults.GroundReverseSpeedCapPercentOfWater;

        /// <summary>
        /// EN: Treat very shallow water as "grounded" for speed caps (escape/reverse limit), based on (waterSurface - floor) depth.
        /// RU: Считать очень мелкую воду "мелью" для скоростных капов (escape/лимит заднего), по глубине (уровень воды - дно).
        /// </summary>
        public static float ShallowWaterDepthForGroundSpeedCaps => GetShallowWaterDepthForGroundSpeedCaps();
        private static float GetShallowWaterDepthForGroundSpeedCaps() => ShipController.PhysicsDefaults.ShallowWaterDepthForGroundSpeedCaps;

        /// <summary>
        /// EN: Extra acceleration multiplier when throttle opposes current motion.
        /// RU: Доп. множитель ускорения, когда газ против текущего движения.
        /// </summary>
        public static float OpposingThrottleAccelMul => GetOpposingThrottleAccelMul();
        private static float GetOpposingThrottleAccelMul() => ShipController.PhysicsDefaults.OpposingThrottleAccelMul;

        /// <summary>
        /// EN: Extra braking factor for opposing throttle (multiplies reverse/brake behavior).
        /// RU: Доп. торможение при противоположном газе.
        /// </summary>
        public static float OpposingThrottleBrakeTuneMul => GetOpposingThrottleBrakeTuneMul();
        private static float GetOpposingThrottleBrakeTuneMul() => ShipController.PhysicsDefaults.OpposingThrottleBrakeTuneMul;

        /// <summary>
        /// EN: Steering responsiveness multiplier.
        /// RU: Множитель отзывчивости руля.
        /// </summary>
        public static float SteeringResponsivenessMul => GetSteeringResponsivenessMul();
        private static float GetSteeringResponsivenessMul() => ShipController.PhysicsDefaults.SteeringResponsivenessMul;

        /// <summary>
        /// EN: Counter-steer responsiveness multiplier.
        /// RU: Множитель отзывчивости контрруления.
        /// </summary>
        public static float CounterSteerResponsivenessMul => GetCounterSteerResponsivenessMul();
        private static float GetCounterSteerResponsivenessMul() => ShipController.PhysicsDefaults.CounterSteerResponsivenessMul;

        /// <summary>
        /// EN: Minimum turning factor at zero speed.
        /// RU: Минимальный фактор поворота на нулевой скорости.
        /// </summary>
        public static float MinTurnFactorAtZeroSpeed => GetMinTurnFactorAtZeroSpeed();
        private static float GetMinTurnFactorAtZeroSpeed() => ShipController.PhysicsDefaults.MinTurnFactorAtZeroSpeed;

        /// <summary>
        /// EN: Speed at which turning reaches 100% (ship speed units).
        /// RU: Скорость, при которой поворот достигает 100% (в игровых единицах скорости).
        /// </summary>
        public static float TurnFullFactorAtSpeed => GetTurnFullFactorAtSpeed();
        private static float GetTurnFullFactorAtSpeed() => ShipController.PhysicsDefaults.TurnFullFactorAtSpeed;

        /// <summary>
        /// EN: Fraction of speed removed at max yaw rate.
        /// RU: Доля снижения скорости на максимальной скорости поворота.
        /// </summary>
        public static float TurnSpeedSlowdownFrac => GetTurnSpeedSlowdownFrac();
        private static float GetTurnSpeedSlowdownFrac() => ShipController.PhysicsDefaults.TurnSpeedSlowdownFrac;

        /// <summary>
        /// EN: Response for TurnSpeedVelocityMul smoothing (lambda).
        /// RU: Скорость сглаживания TurnSpeedVelocityMul (lambda).
        /// </summary>
        public static float TurnSpeedVelocityMulResponse => GetTurnSpeedVelocityMulResponse();
        private static float GetTurnSpeedVelocityMulResponse() => ShipController.PhysicsDefaults.TurnSpeedVelocityMulResponse;

        /// <summary>
        /// EN: Minimum submergence to start upright stabilization (meters).
        /// RU: Минимальное погружение для выравнивания корпуса (метры).
        /// </summary>
        public static float UprightStabilizeMinSubmergedMeters => GetUprightStabilizeMinSubmergedMeters();
        private static float GetUprightStabilizeMinSubmergedMeters() => ShipController.PhysicsDefaults.UprightStabilizeMinSubmergedMeters;

        /// <summary>
        /// EN: Max upright correction angular speed (rad/s).
        /// RU: Макс. скорость выравнивания (рад/с).
        /// </summary>
        public static float UprightStabilizeMaxRadPerSec => GetUprightStabilizeMaxRadPerSec();
        private static float GetUprightStabilizeMaxRadPerSec() => ShipController.PhysicsDefaults.UprightStabilizeMaxRadPerSec;

        /// <summary>
        /// EN: Dead-zone for upright correction (radians).
        /// RU: Мёртвая зона выравнивания (радианы).
        /// </summary>
        public static float UprightStabilizeAngleDeadZoneRad => GetUprightStabilizeAngleDeadZoneRad();
        private static float GetUprightStabilizeAngleDeadZoneRad() => ShipController.PhysicsDefaults.UprightStabilizeAngleDeadZoneRad;

        /// <summary>
        /// EN: Wind cone half-angle (degrees).
        /// RU: Полуугол конуса ветра (градусы).
        /// </summary>
        public static float WindConeHalfAngleDeg => GetWindConeHalfAngleDeg();
        private static float GetWindConeHalfAngleDeg() => ShipController.PhysicsDefaults.WindConeHalfAngleDeg;

        /// <summary>
        /// EN: Max speed multiplier with wind.
        /// RU: Макс. множитель скорости по ветру.
        /// </summary>
        public static float WindWithMaxMul => GetWindWithMaxMul();
        private static float GetWindWithMaxMul() => ShipController.PhysicsDefaults.WindWithMaxMul;

        /// <summary>
        /// EN: Max speed multiplier against wind.
        /// RU: Макс. множитель скорости против ветра.
        /// </summary>
        public static float WindAgainstMaxMul => GetWindAgainstMaxMul();
        private static float GetWindAgainstMaxMul() => ShipController.PhysicsDefaults.WindAgainstMaxMul;
    }

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

        var cap = ShipControllerTuning.GroundEscapeMaxSpeedAbs;

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
        var hy = HullBoxTuning.GetSizeZ(model.MassBoxSizeZ) * scale * 0.5f;
        var hz = model.MassBoxSizeY * scale * 0.5f;

        // Use the already-synced Transform rotation (derived from physics via SyncTransformWithRigidBody)
        // to avoid left/right inversion from phys<->game basis reflections.
        var rotGame = GetTransformRotationMatrix(ship.Transform.Local.Rotation);

        var posGame0 = PhysToGame(rb.Position);
        var offsetLocalPhys = new JVector(
            model.MassCenterX * scale,
            HullBoxTuning.GetCenterZ(model.MassCenterZ, model.MassBoxSizeZ) * scale,
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
        var hy = HullBoxTuning.GetSizeZ(model.MassBoxSizeZ) * scale * 0.5f;
        var hz = model.MassBoxSizeY * scale * 0.5f;

        // See UpdateShipAxisMarkers comment above.
        var rotGame = GetTransformRotationMatrix(ship.Transform.Local.Rotation);

        var posGame0 = PhysToGame(rb.Position);
        var offsetLocalPhys = new JVector(
            model.MassCenterX * scale,
            HullBoxTuning.GetCenterZ(model.MassCenterZ, model.MassBoxSizeZ) * scale,
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
        if (!Enabled)
            return;

        // Extend "contact active" window; TickShip will emit start/end messages.
        var nowMs = Environment.TickCount64;
        const int holdMs = 800;
        _shipContactUntilMs[a.Id] = nowMs + holdMs;
        _shipContactUntilMs[b.Id] = nowMs + holdMs;

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
        if (!Enabled)
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
            return;

        var templateId = ShoreMarkerTemplateId;
        if (templateId == 0 || ship.ParentWorld is null || ship.Transform is null)
        {
            DespawnMarkers(_shoreMarkers, ship.Id);
            return;
        }

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

        // Optional detail line while penetrating
        if (penetrationMeters > 0.0f)
        {
            var driver = TryGetDriver(ship);
            if (driver != null && CanReceive(driver))
            {
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
        }
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

    private static JVector Rotate(JVector v, JQuaternion q)
    {
        var qx = q.X;
        var qy = q.Y;
        var qz = q.Z;
        var qw = q.W;
        var tx = 2f * (qy * v.Z - qz * v.Y);
        var ty = 2f * (qz * v.X - qx * v.Z);
        var tz = 2f * (qx * v.Y - qy * v.X);
        return new JVector(
            v.X + qw * tx + (qy * tz - qz * ty),
            v.Y + qw * ty + (qz * tx - qx * tz),
            v.Z + qw * tz + (qx * ty - qy * tx));
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

