using System.Numerics;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.StreamAoi;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;

using MySql.Data.MySqlClient;

using static AAEmu.Game.Models.Game.Units.Buffs;

namespace AAEmu.Game.Models.Game.Units;

public class Slave : Unit
{
    public override UnitTypeFlag TypeFlag { get => UnitTypeFlag.Slave; }
    public override BaseUnitType BaseUnitType => BaseUnitType.Slave;
    public override ModelPostureType ModelPostureType { get => ModelPostureType.TurretState; }
    //public uint Id { get; set; } // moved to BaseUnit
    //public uint TemplateId { get; set; } // moved to BaseUnit
    public uint BondingObjId { get; set; } = 0;

    public SlaveTemplate Template { get; set; }

    /// <summary>
    /// Sea hulls (and player Leviathan kind) use Ship 225/248. Farm haulers use Ambient.
    /// SlaveKind equipment (sails/cannons as units) is Part: no soft unit cull. Doodad sails
    /// are not slaves — they stay until region leave.
    /// </summary>
    public StreamAoiCategory StreamAoiCategory =>
        Template?.StreamAoiCategory ?? StreamAoiCategory.Ambient;
    // public Character Driver { get; set; }
    public Character Summoner { get; set; }
    public BaseUnitType OwnerType { get; init; }

    /// <summary>
    /// Set for the initial Spawn broadcast when CS/skill did not request hideSpawnEffect; clear
    /// immediately after so AOI re-streams do not replay the portal.
    /// </summary>
    public bool PendingSpawnPortal { get; set; }

    public Item SummoningItem { get; init; }
    public List<Doodad> AttachedDoodads { get; set; }
    public List<Slave> AttachedSlaves { get; set; }
    public Dictionary<AttachPointKind, Character> AttachedCharacters { get; set; }
    public DateTime SpawnTime { get; init; }
    public sbyte ThrottleRequest { get; set; }
    public sbyte Throttle { get; set; }
    public float Speed { get; set; }
    public sbyte SteeringRequest { get; set; }
    public sbyte Steering { get; set; }
    public float RotSpeed { get; set; }
    public short RotationZ { get; set; }
    public float RotationDegrees { get; set; }
    public sbyte AttachPointId { get; init; } = -1;
    public uint OwnerObjId { get; init; }

    /// <summary>
    /// Signed server-world selector for the slave's master. Slaves are local to their summoner by default.
    /// </summary>
    public sbyte MasterWorldId { get; set; } = -1;
    public SlaveSpawner Spawner { get; set; }
    public Task LeaveTask { get; set; }
    public CancellationTokenSource CancelTokenSource { get; set; }

    /// <summary>
    /// Stops a pending leave-world despawn timer if one was armed.
    /// Hulls that never started that timer (GM spawn, still-summoned boat) leave this null.
    /// </summary>
    public void CancelPendingLeave() => CancelTokenSource?.Cancel();
    /// <summary>Ship harpoon rope / skill-controller sync (only meaningful for harpoon cannon slaves; default struct = disengaged, no heap alloc).</summary>
    public ShipHarpoonRopeState HarpoonRope;

    /// <summary>
    /// keeps streaming its own ShipMoveType, so the World mirror (and every client) flip-flops
    /// between two headings and skill impulses land in the wrong process.
    /// </summary>
    public uint ZoneAnnouncedTo { get; set; }

    private long _lastLoggedKitAddedMass = long.MinValue;

    /// <summary>
    /// Last hull pose the simulating zone reported, and the source of everything replayed to the next
    /// simulator on a handoff — position, heading, throttle, steering, rpm and motion
    /// (see <see cref="ShipPoseSeed"/>).
    /// </summary>
    public ShipMoveType SimulatedShipState { get; set; }

    /// <summary>
    /// When <see cref="SimulatedShipState"/> was last written (<see cref="Environment.TickCount64"/>).
    /// A seam handoff freezes that report as <see cref="SeamHandoff"/> so Create and the type-4
    /// seed both advance the same snapshot once.
    /// </summary>
    public long SimulatedShipStateAtMs { get; set; }

    /// <summary>
    /// The report before <see cref="SimulatedShipState"/>, used only to derive acceleration for a
    /// seam snapshot. Null until two poses have arrived from the same stretch.
    /// </summary>
    public ShipMoveType PreviousSimulatedShipState { get; set; }

    /// <summary>When <see cref="PreviousSimulatedShipState"/> was written.</summary>
    public long PreviousSimulatedShipStateAtMs { get; set; }

    /// <summary>
    /// Frozen Zone-A state for the in-flight seam. Create and the type-4 seed both propagate this
    /// once to the activation tick. Replaced (and the epoch bumped) on the next handoff.
    /// </summary>
    public BoatSeamHandoffSnapshot? SeamHandoff { get; set; }

    /// <summary>Handoff sequence for <see cref="SeamHandoff"/>. Stale warmups from an older epoch are ignored.</summary>
    public uint SeamHandoffEpoch { get; set; }

    /// <summary>Helm stick samples taken while <see cref="SeamHandoff"/> is live.</summary>
    public List<BoatSeamHelmSample> SeamHelmQueue { get; } = [];

    /// <summary>
    /// Last type-4 zone id / time / steering written onto <c>SCUnitMovements</c>.
    /// Follow-switch must not change the streamed zone id (client interpolator reset)
    /// or send a behind clock (dropped sample). See <see cref="BoatRudderSeamRules"/>.
    /// </summary>
    public ushort StreamedShipZoneId { get; set; }

    public uint StreamedShipTime { get; set; }

    public sbyte StreamedShipSteering { get; set; }

    /// <summary>
    /// Offset that maps the current simulator's body clock onto the streamed clock
    /// (<see cref="BoatRudderSeamRules.RebasedTime"/>).
    /// </summary>
    public uint StreamedShipTimeOffset { get; set; }

    /// <summary><see cref="Environment.TickCount64"/> when the last hull body was streamed.</summary>
    public long StreamedShipAtMs { get; set; }

    /// <summary>
    /// Speed the simulating zone is actually making the hull travel, in metres per second, measured
    /// from the positions it reports. Zero until two poses have arrived.
    /// </summary>
    public float SimulatedSpeed { get; set; }

    /// <summary>
    /// When <see cref="SimulatedSpeed"/> was measured (<see cref="Environment.TickCount64"/> scale),
    /// so a seam restore can tell fresh way-on from a stale figure left over from before the hull
    /// stopped or docked.
    /// </summary>
    public long SimulatedSpeedAtMs { get; set; }

    /// <summary>
    /// Post-seam speed samples still to be reported, so one crossing yields the speed the hull kept
    /// rather than an impression of it. Set when a new simulator is armed, counted down as the samples
    /// arrive.
    /// </summary>
    public int SeamSpeedProbes { get; set; }

    /// <summary>
    /// Speed the hull was making when simulation was handed to a new zone. Held until the receiving
    /// body has left the interpolation window so a shortfall can be measured. Zero when nothing is
    /// outstanding.
    /// </summary>
    public float SeamTargetSpeed { get; set; }

    /// <summary>Zone the outstanding <see cref="SeamTargetSpeed"/> correction belongs to.</summary>
    public uint SeamCorrectionZone { get; set; }

    /// <summary>
    /// Tick when the new dedicate was armed. Unconsumed outbound type-4 (0–0.2 m/s) is ignored
    /// until the incoming body publishes the restored cruise.
    /// </summary>
    public long SeamArmedAtMs { get; set; }

    /// <summary>
    /// When the closed-loop seam impulse was sent. Zero until then. Follow waits for the
    /// restored speed after this, not for the short pose that triggered the impulse.
    /// </summary>
    public long SeamImpulseAtMs { get; set; }

    /// <summary>
    /// First tick a cruise-speed body was still short of the bridged plant. Zero until then.
    /// FollowBackstopMs is counted from this, not from arm — a slow crossing must still catch up.
    /// </summary>
    public long SeamBridgeBehindAtMs { get; set; }

    /// <summary>
    /// When B was given A's live pose so follow can switch. Zero until that type-4 is sent.
    /// </summary>
    public long SeamReplantAtMs { get; set; }

    /// <summary>
    /// Forward speed added to the incoming body so it closes its along-track gap to the streamed
    /// one; taken back at the follow switch. Zero when no catch-up is in flight.
    /// </summary>
    public float SeamCatchUpSpeed { get; set; }

    /// <summary>When <see cref="SeamCatchUpSpeed"/> was applied (<see cref="Environment.TickCount64"/>).</summary>
    public long SeamCatchUpAtMs { get; set; }

    /// <summary>
    /// Follow-switch blend (<see cref="Core.Managers.World.BoatSeamBlendRules"/>): the outgoing
    /// simulator's last streamed body, captured at the switch, and the residual to the incoming
    /// body once its first report arrives. <see cref="SeamBlendStartMs"/> 0 = no blend running.
    /// </summary>
    public long SeamBlendStartMs { get; set; }

    public Movements.ShipMoveType SeamBlendFrom { get; set; }

    public long SeamBlendFromAtMs { get; set; }

    public BoatSeamBlendRules.Offset? SeamBlendOffset { get; set; }

    /// <summary>
    /// Water-body surface Z used at Create (before any keel plant). Recover compares live Z to a
    /// fresh sample when the world still has water; this is the fallback.
    /// </summary>
    public float PlantWaterSurfaceZ { get; set; } = float.NaN;

    /// <summary>
    /// Last waterline recover (<see cref="Environment.TickCount64"/>). Zero until one has run.
    /// </summary>
    public long WaterlineRecoverAtMs { get; set; }

    /// <summary>
    /// Zone has the hull but simulation is off (no-tube). Tube hulls resume on bind;
    /// no-tube stays off and World drives the waterline from type-5.
    /// </summary>
    public bool WaterlineSimHeldOff { get; set; }

    /// <summary>
    /// Last <see cref="SlaveManager.TickHeldWaterlineDrive"/> (<see cref="Environment.TickCount64"/>).
    /// </summary>
    public long WaterlineDriveAtMs { get; set; }

    /// <summary>
    /// Set when <see cref="SlaveManager.Delete"/> starts the despawn portal, so a replace-summon does
    /// not treat this hull as still active while the portal plays.
    /// </summary>
    public bool IsDespawning { get; set; }

    /// <summary>
    /// Set when <see cref="SlaveManager.FinalizeBoatDespawn"/> finishes withdraw + hide + id
    /// release, so a delayed despawn tick cannot run that teardown (and free the ids) twice.
    /// </summary>
    public bool DespawnFinalized { get; set; }

    /// <summary>
    /// Zone key whose dedicate has been told to simulate this hull, so the enable is sent once per
    /// zone instead of on every helm mount. See <see cref="SlaveManager.EnableBoatSimInZone"/>.
    /// </summary>
    public uint ZoneSimEnabledFor { get; set; }

    /// <summary>
    /// Zone waiting for a delayed sim enable. During a seam handoff this is the new dedicate while
    /// <see cref="ZoneAnnouncedTo"/> still names the live one.
    /// </summary>
    public uint ZoneSimPendingFor { get; set; }

    public Slave()
    {
        // Slots go to at least 31 (slave_equip_slots); character gear enum tops out at 27.
        Equipment = new EquipmentContainer(0, SlotType.EquipmentSlave, false, this)
        {
            ContainerSize = 32
        };
        AttachedDoodads = [];
        AttachedSlaves = [];
        AttachedCharacters = [];
        HpTriggerPointsPercent.Add(0);
        HpTriggerPointsPercent.Add(25);
        HpTriggerPointsPercent.Add(50);
        HpTriggerPointsPercent.Add(75);
        HpTriggerPointsPercent.Add(100);
    }

    #region Attributes
    [UnitAttribute(UnitAttribute.Str)]
    public int Str
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Str);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Str))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Dex)]
    public int Dex
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Dex);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Dex))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Sta)]
    public int Sta
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Sta);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Sta))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Int)]
    public int Int
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Int);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Int))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Spi)]
    public int Spi
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Spi);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Spi))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Fai)]
    public int Fai
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Fai);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Fai))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.MaxHealth)]
    public override int MaxHp
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.MaxHealth);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MaxHealth))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.HealthRegen)]
    public override int HpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.HealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            res += Spi / 10;
            foreach (var bonus in GetBonuses(UnitAttribute.HealthRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.PersistentHealthRegen)]
    public override int PersistentHpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.PersistentHealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.MaxMana)]
    public override int MaxMp
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.MaxMana);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MaxMana))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.ManaRegen)]
    public override int MpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.ManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            res += Spi / 10;
            foreach (var bonus in GetBonuses(UnitAttribute.ManaRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.PersistentManaRegen)]
    public override int PersistentMpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.PersistentManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.PersistentManaRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    public override float LevelDps
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.LevelDps);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["ab_level"] = 0
            };
            var res = formula.Evaluate(parameters);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MainhandDps)]
    public override int Dps
    {
        get
        {
            var weapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand) as Weapon;
            var res = weapon?.Dps ?? 0;
            res += Str / 10f;
            foreach (var bonus in GetBonuses(UnitAttribute.MainhandDps))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }

            return (int)(res * 1000);
        }
    }

    [UnitAttribute(UnitAttribute.MeleeDpsInc)]
    public override int DpsInc
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.MeleeDpsInc);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MeleeDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += res * bonus.Value / 100f;
                else
                    res += (int)bonus.Value;
            }

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.OffhandDps)]
    public override int OffhandDps
    {
        get
        {
            var weapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand) as Weapon;
            var res = weapon?.Dps ?? 0;
            res += Str / 10f;
            foreach (var bonus in GetBonuses(UnitAttribute.OffhandDps))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }

            return (int)(res * 1000);
        }
    }

    [UnitAttribute(UnitAttribute.RangedDps)]
    public override int RangedDps
    {
        get
        {
            var weapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Ranged) as Weapon;
            var res = weapon?.Dps ?? 0;
            res += Dex / 10f;
            foreach (var bonus in GetBonuses(UnitAttribute.RangedDps))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }

            return (int)(res * 1000);
        }
    }

    [UnitAttribute(UnitAttribute.RangedDpsInc)]
    public override int RangedDpsInc
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.RangedDpsInc);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.RangedDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += res * bonus.Value / 100f;
                else
                    res += (int)bonus.Value;
            }

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellDps)]
    public override int MDps
    {
        get
        {
            var weapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand) as Weapon;
            var res = weapon?.MDps ?? 0;
            res += Int / 10f;
            foreach (var bonus in GetBonuses(UnitAttribute.SpellDps))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }

            return (int)(res * 1000);
        }
    }

    [UnitAttribute(UnitAttribute.SpellDpsInc)]
    public override int MDpsInc
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.SpellDpsInc);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.SpellDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += res * bonus.Value / 100f;
                else
                    res += (int)bonus.Value;
            }

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Armor)]
    public override int Armor
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Armor);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Armor))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.MagicResist)]
    public override int MagicResistance
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.MagicResist);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MagicResist))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.TurnSpeed)]
    public virtual float TurnSpeed { get => (float)CalculateWithBonuses(100f, UnitAttribute.TurnSpeed) / 100f; }

    #endregion

    /// <summary>
    /// Ship parts have no effects of their own; item_grade_buffs maps the equipped (itemId, grade) pair to a
    /// buff that belongs on the hull (all of them are flagged slave_applicable). That buff is where a sail's
    /// move-speed / turn-speed unit_modifiers live, and it is what a figurehead's mount skills name in their
    /// unit_reqs (kind 15) before they may be used at the helm.
    /// Called with both arguments null to (re)build the whole set, e.g. after a summon restores saved gear.
    /// </summary>
    public void UpdateEquipmentBuffs(Item itemAdded, Item itemRemoved)
    {
        if (itemRemoved != null)
        {
            // Withdraw whichever tier is actually on the hull, not whichever the remaining piece count
            // would now choose — those differ the moment a paired part loses one of its copies.
            var remaining = EquippedCount(itemRemoved.TemplateId);
            var stillEarnedId = remaining > 0 ? GetEquipmentBuff(itemRemoved)?.Id ?? 0 : 0;
            foreach (var buffId in ItemGameData.Instance.GetItemBuffIds(itemRemoved.TemplateId, itemRemoved.Grade))
            {
                if (!Buffs.CheckBuff(buffId))
                    continue;
                if (EquipmentBuffRules.KeepWithdrawnBuff(remaining, stillEarnedId, buffId))
                    continue;
                Buffs.RemoveBuff(buffId);
            }

            // Re-apply at the lower tier when copies remain (a paired part dropping 2 pieces to 1).
            if (EquippedCount(itemRemoved.TemplateId) > 0)
                ApplyEquipmentBuff(itemRemoved);
        }

        if (itemAdded != null)
        {
            ApplyEquipmentBuff(itemAdded);
            return;
        }

        if (itemRemoved != null)
            return;

        foreach (var item in Equipment.Items)
            ApplyEquipmentBuff(item);
    }

    /// <summary>Copies of this item template currently fitted to the hull.</summary>
    private int EquippedCount(uint templateId) =>
        Equipment.Items.Count(i => i != null && i.TemplateId == templateId);

    /// <summary>
    /// The buff this part grants at its grade, for the number of copies of it the hull carries.
    /// </summary>
    /// <remarks>
    /// The piece count matters: a sail's one-piece row carries its <c>move_speed_mul</c> while its
    /// two-piece row is an identically named buff with no modifiers, so ignoring it can hand a rigged
    /// hull none of its sail bonuses.
    /// </remarks>
    private BuffTemplate GetEquipmentBuff(Item item)
    {
        if (item == null)
            return null;

        var count = EquippedCount(item.TemplateId);
        if (count <= 0)
            return null;

        return ItemGameData.Instance.GetItemBuff(item.TemplateId, item.Grade, count) ??
               SkillManager.Instance.GetBuffTemplate(item.Template?.BuffId ?? 0);
    }

    private void ApplyEquipmentBuff(Item item)
    {
        var buffTemplate = GetEquipmentBuff(item);
        if (buffTemplate == null)
            return;

        foreach (var otherId in ItemGameData.Instance.GetItemBuffIds(item.TemplateId))
        {
            if (!Buffs.CheckBuff(otherId))
                continue;
            var copies = CopiesEarningBuff(item.TemplateId, otherId, item);
            if (!EquipmentBuffRules.StripOtherGrade(buffTemplate.Id, otherId, copies))
                continue;
            Buffs.RemoveBuff(otherId);
        }

        if (Buffs.CheckBuff(buffTemplate.Id))
            return;

        Buffs.AddBuff(new Buff(this, this, new SkillCasterUnit(ObjId), buffTemplate, null, DateTime.UtcNow)
        {
            AbLevel = (uint)(item.Template?.Level ?? 1)
        });
    }

    /// <summary>
    /// Fitted copies of <paramref name="templateId"/>, other than <paramref name="except"/>,
    /// that currently earn <paramref name="buffId"/>.
    /// </summary>
    private int CopiesEarningBuff(uint templateId, uint buffId, Item except)
    {
        var n = 0;
        foreach (var fitted in Equipment.Items)
        {
            if (fitted == null || fitted.TemplateId != templateId || ReferenceEquals(fitted, except))
                continue;
            if (GetEquipmentBuff(fitted)?.Id == buffId)
                n++;
        }

        return n;
    }

    public override void AddVisibleObject(Character character)
    {
        if (character.CanStreamSlaveNow(this))
            SendUnitStateTo(character);
        else
            character.EnqueuePendingSlave(this);

        // Children (sail doodads, equipment slaves) still paint with region interest.
        // Soft hull cull must not reverse that via RemoveVisibleObject.
        base.AddVisibleObject(character);
    }

    /// <summary>
    /// Cinema / teleport resend. The client dropped what it had, so the slot is released and
    /// the hull is sent again — at exit-band eligibility when it was already streamed and is
    /// still inside that band, otherwise through the normal enter-band path.
    /// </summary>
    public void ResendVisibleObject(Character character)
    {
        if (character == null)
            return;

        var repaint = character.ShouldRepaintStreamedSlave(this);
        character.ReleaseSlaveSlot(ObjId);
        if (!repaint)
        {
            AddVisibleObject(character);
            return;
        }

        SendUnitStateTo(character);
        base.AddVisibleObject(character);
    }

    /// <summary>
    /// Hull SCUnitState + points + slave-state + faction. Marks the character's slave stream
    /// slot. Does not walk children.
    /// </summary>
    public void SendUnitStateTo(Character character)
    {
        if (character == null || ObjId == 0)
            return;
        if (character.StreamedSlaveIds.ContainsKey(ObjId))
            return;

        character.SendPacket(new SCUnitStatePacket(this));
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        character.SendPacket(new SCSlaveStatePacket(ObjId, TlId, Summoner?.Name ?? string.Empty, Summoner?.ObjId ?? 0, Id));

        // Same gate as Npc: SCUnitState does not carry faction for non-characters, and the
        // client only applies 0x02E when oldId matches current (fresh units are 0). Without
        // old=Invalid → new=real, summoned vehicles stay yellow/neutral.
        if (Faction != null)
            character.SendPacket(new SCUnitFactionChangedPacket(
                ObjId, Name ?? "", FactionsEnum.Invalid, Faction.Id, false));

        character.MarkSlaveStreamed(this);

        foreach (var ati in AttachedCharacters)
        {
            if (ati.Value.ObjId > 0)
            {
                var player = WorldManager.Instance.GetCharacterByObjId(ati.Value.ObjId);
                if (player != null)
                {
                    var reason = character.ObjId == player.ObjId && ati.Key == AttachPointKind.Driver
                        ? AttachUnitReason.NewMaster
                        : AttachUnitReason.None;
                    character.SendPacket(new SCUnitAttachedPacket(player.ObjId, ati.Key, reason, ObjId));
                }
            }
        }
    }

    public override void RemoveVisibleObject(Character character)
    {
        if (BoatHelmSeatRules.ShouldKeepStreamedHullForRider(character.IsRidingSlave(this)))
            return;

        character.ReleaseSlaveSlot(ObjId);

        // Region leave: base walks Transform.Children (sails/cannons). Soft Ship-band cull
        // of the selectable hull must not use this path — those doodads linger commercially.
        base.RemoveVisibleObject(character);

        character.SendPacket(new SCUnitsRemovedPacket([ObjId]));
    }

    public override void PostUpdateCurrentHp(BaseUnit attacker, int oldHpValue, int newHpValue, KillReason killReason = KillReason.Damage)
    {
        base.PostUpdateCurrentHp(attacker, oldHpValue, newHpValue, killReason);
    }

    protected override void DoHpChangeTrigger(int triggerValue, bool tookDamage, int oldHpValue, int newHpValue)
    {
        Logger.Debug($"{Name} from {Summoner?.Name ?? "unknown"}'s HP is now at {triggerValue}%");
        ParentWorld.SlaveManager.UpdateSlaveRepairPoints(this);
    }

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        InterruptSkills();
        Events.OnDeath(this, new OnDeathArgs { Killer = (Unit)killer, Victim = this });
        Buffs.RemoveEffectsOnDeath();
        killer.BroadcastPacket(new SCUnitDeathPacket(ObjId, killReason, (Unit)killer), true);

        DestroyAttachedItems();
        DistributeSlaveDropDoodads();
        MarkSummoningItemAsDestroyed();

        Summoner?.SendPacket(new SCMySlavePacket(ObjId, TlId, Name, TemplateId, Hp, MaxHp, Transform.World.Position.X, Transform.World.Position.Y, Transform.World.Position.Z));
        SlaveManager.SendUpdatedSlaveSourceItem(Summoner, this);
        Summoner?.BroadcastPacket(new SCSlaveRemovedPacket(Summoner.ObjId, TlId), true);
        ClearAllAggro();

        // Unbind all passengers
        foreach (var character in AttachedCharacters.Values.ToList())
            ParentWorld.SlaveManager.UnbindSlave(character, TlId, AttachUnitReason.None);

        // Schedule full cleanup via slave.Delete() → Hide() + DetachAll() + RemoveObject()
        // This keeps the slave visible and selectable during the death animation
        Despawn = DateTime.UtcNow.AddSeconds(Spawner?.DespawnTime ?? 20);
        ParentWorld.SpawnManager.AddDespawn(this);
    }

    /// <summary>
    /// Destroys (de-spawns) any child doodads and slaves and drops trade packs if present in a random 1m range to the center of the vehicle
    /// </summary>
    private void DestroyAttachedItems()
    {
        // Destroy Doodads
        foreach (var doodad in AttachedDoodads)
        {
            // Check if the doodad held an item
            if (doodad.ItemId > 0)
            {
                var droppedItem = ItemManager.Instance.GetItemByItemId(doodad.ItemId);
                // If the held item is a backpack, drop it to the floor
                if (droppedItem is Backpack backpackItem)
                {
                    // Drop Backpack to the floor (spawn doodad)
                    var putDownSkill = SkillManager.Instance.GetSkillTemplate(backpackItem.Template.UseSkillId);
                    foreach (var skillEffect in putDownSkill.Effects)
                    {
                        if (skillEffect.Template is not PutDownBackpackEffect putDownBackpackEffectTemplate)
                            continue;

                        var newDoodadId = putDownBackpackEffectTemplate.BackpackDoodadId;

                        // Create the Doodad at location on the floor if it's close to it
                        var newDoodad = DoodadManager.Instance.Create(ParentWorld, 0, newDoodadId, null, true);
                        if (newDoodad == null)
                        {
                            Logger.Warn($"Dropped Doodad {newDoodadId}, from BackpackDoodadId could not be created");
                            break;
                        }
                        newDoodad.IsPersistent = true;
                        newDoodad.Transform = doodad.Transform.CloneDetached();
                        // Add a bit of randomness to the dropped doodad
                        newDoodad.Transform.Local.Translate(
                            Random.Shared.NextSingle() * 2f - 1f,
                            Random.Shared.NextSingle() * 2f - 1f,
                            0f);
                        newDoodad.AttachPoint = AttachPointKind.None;
                        newDoodad.ItemId = droppedItem.Id;
                        newDoodad.ItemTemplateId = droppedItem.TemplateId;
                        newDoodad.UccId = droppedItem.UccId; // Not sure if it's needed, but let's copy the Ucc for completeness' sake
                        newDoodad.SetScale(1f);
                        newDoodad.PlantTime = DateTime.UtcNow;
                        newDoodad.Faction = FactionManager.Instance.GetFaction(FactionsEnum.Friendly);

                        var floor = ParentWorld.Template.GeoData.GetHeight(newDoodad.Transform.World.Position); // WorldManager.Instance.GetHeight(newDoodad.Transform);
                        var surface = WorldManager.Instance.GetWorld(doodad.Transform.InstanceId)?.Water?.GetWaterSurface(newDoodad.Transform.World.Position, out _) ?? 0f;
                        var depth = surface - floor;

                        // It seems that when the water is deep, drops to the water surface, otherwise, it sinks to the floor
                        // Requires more testing, possibly a server setting?
                        newDoodad.Transform.Local.SetHeight(depth < 30f ? floor : Math.Max(floor, surface));

                        // Save new doodad
                        newDoodad.InitDoodad();
                        newDoodad.Spawn();
                        newDoodad.Save();

                        if (WorldIntegration.ZoneAuthority)
                        {
                            var p = newDoodad.Transform.World.Position;
                            WorldIntegration.RelayDropBackpackToZone?.Invoke(
                                ObjId, droppedItem, newDoodadId, Transform.ZoneId,
                                p.X, p.Y, p.Z, true, false, false);
                        }

                        // Remove data from trade pack slot
                        doodad.ItemTemplateId = 0;
                        doodad.ItemId = 0;

                        // Hacky way to force move to next phase to reset doodad to default before saving
                        var funcs = DoodadManager.Instance.GetDoodadFuncs(doodad.FuncGroupId);
                        foreach (var phaseFunc in funcs)
                        {
                            if (phaseFunc.FuncType == "DoodadFuncRecoverItem")
                            {
                                doodad.DoChangePhase(null, phaseFunc.NextPhase);
                                break;
                            }
                        }

                        // Save new empty data
                        doodad.Save();
                    }
                }
            }
            NonUnitObjectIdManager.Instance.ReleaseId(doodad.ObjId);
            doodad.IsPersistent = false;
            doodad.Delete();
        }

        // Destroy Slaves
        foreach (var slave in AttachedSlaves)
        {
            ObjectIdManager.Instance.ReleaseId(slave.ObjId);
            // slave.IsPersistent = false;
            slave.Delete();
        }
    }

    /// <summary>
    /// Creates the random debris created by destroying some of the vehicles (mostly ships)
    /// </summary>
    private void DistributeSlaveDropDoodads()
    {
        foreach (var dropDoodad in Template.SlaveDropDoodads)
        {
            for (var counter = 0; counter < dropDoodad.Count; counter++)
            {
                var doodad = DoodadManager.Instance.Create(ParentWorld, 0, dropDoodad.DoodadId, null, true);
                var pos = Transform.World.Position;
                var rng = new Vector3(Random.Shared.NextSingle() * 2f - 1f, Random.Shared.NextSingle() * 2f - 1f, 0);
                rng = Vector3.Normalize(rng);
                rng *= Random.Shared.NextSingle() * dropDoodad.Radius;
                pos += rng;
                doodad.Transform.Local.SetPosition(pos);
                if (dropDoodad.OnWater == false)
                {
                    doodad.Transform.Local.SetHeight(doodad.ParentWorld.Template.GeoData.GetHeight(doodad.Transform.World.Position)); //WorldManager.Instance.GetHeight(doodad.Transform.ZoneId, pos.X, pos.Y, pos.Z));
                }
                else
                {
                    doodad.Transform.Local.SetHeight(WorldManager.Instance.GetWorld(doodad.Transform.InstanceId).Water.GetWaterSurface(pos, out _));
                }
                doodad.Transform.Local.Rotate(0, 0, (float)(Random.Shared.NextDouble() * Math.PI * 2f));
                doodad.InitDoodad();
                doodad.Spawn();
            }
        }
    }

    /// <summary>
    /// Updates the summon item data as being destroyed
    /// </summary>
    private void MarkSummoningItemAsDestroyed()
    {
        if (SummoningItem is not SummonSlave item)
            return;
        item.IsDestroyed = 1;
        item.RepairStartTime = DateTime.MinValue;
        item.SummonLocation = Vector3.Zero;
        item.IsDirty = true;
        Summoner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.MateDeath, new ItemUpdate(item), []));
    }

    /// <summary>
    /// Creates a new DB connection and calls the Save function
    /// </summary>
    /// <returns></returns>
    public bool Save()
    {
        if (Id <= 0 || SummoningItem == null)
            return false;

        using var connection = MySQL.CreateConnection();
        return Save(connection, null);
    }

    /// <summary>
    /// Saves vehicle data to DB
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="transaction"></param>
    /// <returns></returns>
    public bool Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (Id <= 0)
            return false;

        bool result;
        try
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            if (transaction != null)
                command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO slaves(`id`,`item_id`,`template_id`,`attach_point`,`name`,`owner_type`,`owner_id`,`summoner`,`updated_at`,`hp`,`mp`,`x`,`y`,`z`) " +
                "VALUES (@id, @item_id, @templateId, @attachPoint, @name, @ownerType, @ownerId, @owner, @updated_at, @hp, @mp, @x, @y, @z)";
            command.Parameters.AddWithValue("@id", Id);
            command.Parameters.AddWithValue("@item_id", SummoningItem?.Id ?? 0);
            command.Parameters.AddWithValue("@templateId", Template.Id);
            command.Parameters.AddWithValue("@attachPoint", AttachPointId);
            command.Parameters.AddWithValue("@ownerType", (byte)OwnerType);
            command.Parameters.AddWithValue("@ownerId", OwnerId);
            command.Parameters.AddWithValue("@owner", Summoner?.Id ?? 0);
            command.Parameters.AddWithValue("@name", Name);
            command.Parameters.AddWithValue("@hp", Hp);
            command.Parameters.AddWithValue("@mp", Mp);
            command.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
            command.Parameters.AddWithValue("@x", Transform.World.Position.X);
            command.Parameters.AddWithValue("@y", Transform.World.Position.Y);
            command.Parameters.AddWithValue("@z", Transform.World.Position.Z);
            command.ExecuteNonQuery();
            result = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            result = false;
        }

        // Also save its children if needed
        foreach (var child in AttachedSlaves)
            if (child.Id > 0)
                child.Save(connection, transaction);

        return result;
    }

    /// <summary>
    /// Moored / Slave Customizing Area (sphere_buffs detail → buffs.id 13817). Flat HealthRegen +200.
    /// Distinct from Ezi's Divine Protection (13816), which is collision/speed — not the dock heal.
    /// </summary>
    public const uint MooredBuffId = 13817;

    /// <summary>
    /// Registers the <c>unit_modifiers</c> of the hull's equipped parts as gear bonuses.
    /// </summary>
    /// <remarks>
    /// Ship parts carry real stats — each Bubbling mast (item 35426) is MaxHealth +5000 — and the
    /// client sums them into the hull bar. Ignoring them server-side left the Growling sailing ship
    /// with MaxHp 85000 against the 95000 the client computed, so a "full" hull drew as 89% and
    /// dock repair had nothing left to heal (both numbers then scale by Ezi's +10%: 93500/104500).
    /// </remarks>
    public void UpdateSlaveGearBonuses()
    {
        Bonuses[GearBonusesIndex] = [];
        if (Equipment != null)
        {
            foreach (var item in Equipment.Items)
            {
                if (item == null)
                    continue;

                foreach (var template in ItemManager.Instance.GetUnitModifiers(item.TemplateId))
                    AddBonus(GearBonusesIndex, new Bonus { Template = template, Value = template.Value });

                if (item is EquipItem equipItem)
                {
                    foreach (var gem in equipItem.GemIds)
                        foreach (var template in ItemManager.Instance.GetUnitModifiers(gem))
                            AddBonus(GearBonusesIndex, new Bonus { Template = template, Value = template.Value });
                }
            }
        }

        if (AttachedSlaves != null)
        {
            foreach (var child in AttachedSlaves)
            {
                if (child?.Template?.Bonuses == null)
                    continue;
                foreach (var template in child.Template.Bonuses)
                {
                    if (template.Attribute != UnitAttribute.Mass)
                        continue;
                    AddBonus(GearBonusesIndex, new Bonus { Template = template, Value = template.Value });
                }
            }
        }

        LogKitAddedMassIfChanged();
    }

    private void LogKitAddedMassIfChanged()
    {
        var itemMass = 0L;
        if (Equipment != null)
        {
            foreach (var item in Equipment.Items)
            {
                if (item == null)
                    continue;
                itemMass += SlaveMassRules.MassFromBonuses(ItemManager.Instance.GetUnitModifiers(item.TemplateId));
            }
        }

        var childMass = 0L;
        if (AttachedSlaves != null)
        {
            foreach (var child in AttachedSlaves)
            {
                if (child?.Template?.Bonuses == null)
                    continue;
                childMass += SlaveMassRules.MassFromBonuses(child.Template.Bonuses);
            }
        }

        var added = SlaveMassRules.KitAddedMass([itemMass], [childMass]);
        if (added == _lastLoggedKitAddedMass)
            return;

        _lastLoggedKitAddedMass = added;
        Logger.Info(
            "Slave kit mass obj={0} tpl={1} added={2} items={3} children={4}",
            ObjId, TemplateId, added, itemMass, childMass);
    }

    protected override void RegenTick(TimeSpan delta)
    {
        if (!NeedsRegen)
        {
            return;
        }
        if (IsDead)
        {
            foreach (var (_, character) in AttachedCharacters)
                character.ParentWorld.SlaveManager.UnbindSlave(character, TlId, AttachUnitReason.None);
            return;
        }

        var oldHp = Hp;

        // Dock Moored heal is HealthRegen (+200). Ship turn skills (Impulse) must not mute it via
        var moored = Buffs.CheckBuff(MooredBuffId);
        if (IsInBattle && !moored)
        {
            Hp += PersistentHpRegen;
            Mp += PersistentMpRegen;
        }
        else
        {
            Hp += HpRegen;
            Mp += MpRegen;
        }

        Hp = Math.Min(Hp, MaxHp);
        Mp = Math.Min(Mp, MaxMp);
        var points = new SCUnitPointsPacket(ObjId, Hp, Mp);
        BroadcastPacket(points, false);
        // Parent/driver can miss GetAround after hull Zone sync moves the ship; always push to riders.
        foreach (var rider in AttachedCharacters.Values)
            rider?.SendPacket(points);
        if (Summoner != null && !AttachedCharacters.ContainsValue(Summoner))
            Summoner.SendPacket(points);
        PostUpdateCurrentHp(this, oldHp, Hp, KillReason.Unknown);
        if (Hp != oldHp)
            SlaveManager.SendUpdatedSlaveSourceItem(Summoner, this);
    }

    public override void OnZoneChange(uint lastZoneKey, uint newZoneKey)
    {
        base.OnZoneChange(lastZoneKey, newZoneKey); // Unit

        // WZ traffic (impulse turns, control changes) is routed by the hull's current zone key, and a
        // announce belongs to the summon path, which sends the fully built state body. Every
        // announced vehicle hands off, not just boats — cars are zone-simulated too (fset bit 177).
        if (ZoneAnnouncedTo != 0 && ZoneAnnouncedTo != newZoneKey)
            SlaveManager.CommitBoatZoneHandoff(this, lastZoneKey, newZoneKey);

        foreach (var passenger in AttachedCharacters)
        {
            passenger.Value?.OnZoneChange(lastZoneKey, newZoneKey);
        }
    }

    public override Character GetOwnerCharacter()
    {
        var ownerObject = Summoner ?? (OwnerObjId > 0 ? ParentWorld.GetGameObject(OwnerObjId) as BaseUnit : null);
        return ownerObject?.GetOwnerCharacter();
    }
}
