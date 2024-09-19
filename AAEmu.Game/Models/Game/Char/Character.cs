using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Attendance;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.FishSchools;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public partial class Character : Unit, ICharacter
{
    public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Character;
    public override BaseUnitType BaseUnitType => BaseUnitType.Character;

    public static Dictionary<uint, uint> UsedCharacterObjIds { get; } = new();

    private Dictionary<ushort, string> _options;

    public List<IDisposable> Subscribers { get; set; }
    public override CharacterEvents Events { get; } = new();
    //public uint Id { get; set; } // moved to BaseUnit
    public ulong AccountId { get; set; }
    public Race Race { get; set; }
    public Gender Gender { get; set; }

    /// <summary>
    /// Cached representation of Account Labor
    /// </summary>
    public short LaborPower
    {
        get => _laborPower;
        set
        {
            if (_laborPower == value)
                return;
            _laborPower = value;
            AccountManager.Instance.UpdateLabor(AccountId, value);
        }
    }
    public short LocalLaborPower { get; set; }

    public DateTime LaborPowerModified
    {
        get => _laborPowerModified;
        set
        {
            if (_laborPowerModified == value)
                return;

            _laborPowerModified = value;
            AccountManager.Instance.UpdateTickTimes(AccountId, value, true, false, false);
        }
    }

    public int ConsumedLaborPower { get; set; }
    public AbilityType Ability1 { get; set; }
    public AbilityType Ability2 { get; set; }
    public AbilityType Ability3 { get; set; }
    public AbilityType HighAbility1 { get; set; }
    public AbilityType HighAbility2 { get; set; }
    public AbilityType HighAbility3 { get; set; }
    public DateTime LastCast { get; set; }
    //public bool IsInCombat { get; set; } // there's already an isInBattle
    public bool IsInPostCast { get; set; }
    public bool IgnoreSkillCooldowns { get; set; }
    public string FactionName { get; set; }
    public string OriginFactionName { get; set; }
    public uint Family { get; set; }
    public short DeadCount { get; set; }
    public DateTime DeadTime { get; set; }
    public int RezWaitDuration { get; set; }
    public DateTime RezTime { get; set; }
    public int RezPenaltyDuration { get; set; }
    public DateTime LeaveTime { get; set; }
    public long Money { get; set; }
    public long Money2 { get; set; }
    public int HonorPoint { get; set; }
    public int VocationPoint { get; set; }
    public short CrimePoint { get; set; }
    public int CrimeRecord { get; set; }
    public short CrimeScore { get; set; }
    public int JuryPoint { get; set; }
    public DateTime DeleteRequestTime { get; set; }
    public DateTime TransferRequestTime { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime DeleteTime { get; set; }
    public DateTime RechargeResetTime { get; set; }

    /// <summary>
    /// Cache value of AccountDetails.Loyalty
    /// </summary>
    public long BmPoint { get; set; }
    public short RechargedLp { get; set; }
    public bool AutoUseAAPoint { get; set; }
    public int PrevPoint { get; set; }
    public int Point { get; set; }
    public int Gift { get; set; }
    public int Experience { get; set; }
    public int RecoverableExp { get; set; }
    public DateTime Created { get; set; } // время создания персонажа
    public DateTime Updated { get; set; } // время внесения изменений
    public byte ForceNameChange { get; set; }

    public uint ReturnDistrictId { get; set; }
    public uint ResurrectionDistrictId { get; set; }

    public override UnitCustomModelParams ModelParams { get; set; }
    public override float Scale => 1f;
    public override byte RaceGender => (byte)(16 * (byte)Gender + (byte)Race);

    public CharacterVisualOptions VisualOptions { get; set; }

    public const int MaxActionSlots = 157; // 85 in 1.2, 121 in 3.0.3.0, 133 in 3.5.0.3, 4.5.1.0, 157 in 5.0+
    public ActionSlot[] Slots { get; set; }
    public Inventory Inventory { get; set; }
    public byte NumInventorySlots { get; set; }
    public short NumBankSlots { get; set; }

    // public Item[] BuyBack { get; set; }
    public ItemContainer BuyBackItems { get; set; }
    public BondDoodad Bonding { get; set; }
    public CharacterQuests Quests { get; set; }
    public CharacterMails Mails { get; set; }
    public CharacterAppellations Appellations { get; set; }
    public CharacterAbilities Abilities { get; set; }
    public CharacterPortals Portals { get; set; }
    public CharacterFriends Friends { get; set; }
    public CharacterBlocked Blocked { get; set; }
    public CharacterMates Mates { get; set; }
    public CharacterAttendances Attendances { get; set; }

    public byte ExpandedExpert { get; set; }
    public CharacterActability Actability { get; set; }

    public CharacterSkills Skills { get; set; }
    public CharacterCraft Craft { get; set; }
    public uint SubZoneId { get; set; } // понадобилось хранить для составления точек Memory Tome (Recall)
    public int AccessLevel { get; set; }
    public TeamPingPos LocalPingPosition { get; set; } // added as a GM command helper
    private ConcurrentDictionary<uint, DateTime> _hostilePlayers { get; set; }
    public bool IsRiding { get; set; }
    public bool SkillCancelled { get; set; }
    /// <summary>
    /// AttachPoint the player currently has in use  
    /// </summary>
    public AttachPointKind AttachedPoint { get; set; }

    /// <summary>
    /// Helper to keep track of what cinema is supposed to play
    /// </summary>
    public uint CurrentlyPlayingCinemaId { get; set; }

    public override bool IsUnderWater
    {
        get { return _isUnderWater; }
        set
        {
            if (_isUnderWater == value) return;
            _isUnderWater = value;
            if (!_isUnderWater)
                Breath = LungCapacity;
            SendPacket(new SCUnderWaterPacket(_isUnderWater));
        }
    }

    private bool _inParty;
    private bool _isOnline;
    private short _laborPower;
    private DateTime _laborPowerModified;

    /// <summary>
    /// List of ObjIds you have aggro on
    /// </summary>
    public Dictionary<uint, BaseUnit> IsInAggroListOf { get; set; } = new();

    public void InitializeLaborCache(short labor, DateTime newTime)
    {
        _laborPower = labor;
        _laborPowerModified = newTime;
    }

    public bool InParty
    {
        get => _inParty;
        set
        {
            if (_inParty == value) return;
            // TODO - GUILD STATUS CHANGE
            FriendMananger.Instance.SendStatusChange(this, false, value);
            _inParty = value;
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline == value) return;
            // TODO - GUILD STATUS CHANGE
            FriendMananger.Instance.SendStatusChange(this, true, value);
            if (!value) TeamManager.Instance.SetOffline(this);
            _isOnline = value;
        }
    }
    public FishSchool FishSchool { get; set; }

    #region Attributes

    [UnitAttribute(UnitAttribute.GlobalCooldownMul)]
    public override float GlobalCooldownMul
    {
        get
        {
            var res = CalculateWithBonuses(0, UnitAttribute.GlobalCooldownMul);

            return (int)(100000f / (res + 1000f));
        }
    }

    [UnitAttribute(UnitAttribute.Str)]
    public int Str
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Str);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            var result = formula.Evaluate(parameters);
            var res = result;
            foreach (var item in Inventory.Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Str;
            res = CalculateWithBonuses(res, UnitAttribute.Str);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Dex)]
    public int Dex
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Dex);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            var res = formula.Evaluate(parameters);
            foreach (var item in Inventory.Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Dex;
            res = CalculateWithBonuses(res, UnitAttribute.Dex);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Sta)]
    public int Sta
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Sta);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            var res = formula.Evaluate(parameters);
            foreach (var item in Inventory.Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Sta;
            res = CalculateWithBonuses(res, UnitAttribute.Sta);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Int)]
    public int Int
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Int);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            var res = formula.Evaluate(parameters);
            foreach (var item in Inventory.Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Int;
            res = CalculateWithBonuses(res, UnitAttribute.Int);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Spi)]
    public int Spi
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Spi);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            var res = formula.Evaluate(parameters);
            foreach (var item in Inventory.Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Spi;
            res = CalculateWithBonuses(res, UnitAttribute.Spi);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Fai)]
    public int Fai
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Fai);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.Fai);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.MaxHealth)]
    public override int MaxHp
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MaxHealth);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            parameters["sta"] = Sta;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MaxHealth);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealthRegen)]
    public override int HpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealthRegen);
            var parameters = new Dictionary<string, double>();
            parameters["sta"] = Sta;
            var res = formula.Evaluate(parameters);
            // res += Spi / 10;
            res = CalculateWithBonuses(res, UnitAttribute.HealthRegen);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.PersistentHealthRegen)]
    public override int PersistentHpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.PersistentHealthRegen);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            parameters["str"] = Str;
            parameters["dex"] = Dex;
            parameters["sta"] = Sta;
            parameters["int"] = Int;
            parameters["spi"] = Spi;
            parameters["fai"] = Fai;
            parameters["heir_level"] = HeirLevel;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.PersistentHealthRegen);
            res /= 5;

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.MaxMana)]
    public override int MaxMp
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MaxMana);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            parameters["int"] = Int;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MaxMana);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.ManaRegen)]
    public override int MpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.ManaRegen);
            var parameters = new Dictionary<string, double>();
            parameters["spi"] = Spi;
            var res = formula.Evaluate(parameters);
            res += Spi / 10;
            res = CalculateWithBonuses(res, UnitAttribute.ManaRegen);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.PersistentManaRegen)]
    public override int PersistentMpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.PersistentManaRegen);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            parameters["str"] = Str;
            parameters["dex"] = Dex;
            parameters["sta"] = Sta;
            parameters["int"] = Int;
            parameters["spi"] = Spi;
            parameters["fai"] = Fai;
            parameters["heir_level"] = HeirLevel;
            var res = formula.Evaluate(parameters);
            res /= 5; // TODO ...
            res = CalculateWithBonuses(res, UnitAttribute.PersistentManaRegen);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingDamageMul)]
    public override float IncomingDamageMul
    {
        get
        {
            double res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingDamageMul);
            res = res / 1000;
            res = 1 + res;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingMeleeDamageMul)]
    public override float IncomingMeleeDamageMul
    {
        get
        {
            double res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingMeleeDamageMul);
            res = CalculateWithBonuses(res, UnitAttribute.IncomingDamageMul);
            res = res / 1000;
            res = 1 + res;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingRangedDamageMul)]
    public override float IncomingRangedDamageMul
    {
        get
        {
            double res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingRangedDamageMul);
            res = CalculateWithBonuses(res, UnitAttribute.IncomingDamageMul);
            res = res / 1000;
            res = 1 + res;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingSpellDamageMul)]
    public override float IncomingSpellDamageMul
    {
        get
        {
            double res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingSpellDamageMul);
            res = CalculateWithBonuses(res, UnitAttribute.IncomingDamageMul);
            res = res / 1000;
            res = 1 + res;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.CastingTimeMul)]
    public override float CastTimeMul
    {
        get
        {
            double res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.CastingTimeMul);
            res = (res + 1000.00000000) / 1000;
            return (float)Math.Max(res, 0f);
        }
    }

    [UnitAttribute(UnitAttribute.MeleeDamageMul)]
    public override float MeleeDamageMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.MeleeDamageMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedDamageMul)]
    public override float RangedDamageMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.RangedDamageMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellDamageMul)]
    public override float SpellDamageMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.SpellDamageMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingHealMul)]
    public override float IncomingHealMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingHealMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealMul)]
    public override float HealMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.HealMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    public override float LevelDps
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.LevelDps);
            var parameters = new Dictionary<string, double>();
            parameters["ab_level"] = Level; // TODO : Make AbilityLevel
            var res = formula.Evaluate(parameters);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MainhandDps)]
    public override int Dps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
            var res = (weapon?.Dps ?? 0) * 1000f;
            res += Str / 5f * 1000f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.MainhandDps);

            return (int)(res);
        }
    }

    [UnitAttribute(UnitAttribute.MeleeDpsInc)]
    public override int DpsInc
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeDpsInc);
            var parameters = new Dictionary<string, double>();
            parameters["str"] = Str;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MeleeDpsInc);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.OffhandDps)]
    public override int OffhandDps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
            var res = weapon?.Dps ?? 0;
            // res += Str / 10f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.OffhandDps);

            return (int)(res * 1000);
        }
    }

    [UnitAttribute(UnitAttribute.RangedDps)]
    public override int RangedDps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Ranged);
            var res = (weapon?.Dps ?? 0) * 1000f;
            res += Dex / 5f * 1000f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.RangedDps);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedDpsInc)]
    public override int RangedDpsInc
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.RangedDpsInc);
            var parameters = new Dictionary<string, double>();
            parameters["dex"] = Dex;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.RangedDpsInc);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellDps)]
    public override int MDps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
            var res = (weapon?.MDps ?? 0) * 1000f;
            res += Int / 5f * 1000f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellDps);

            return (int)(res);
        }
    }

    [UnitAttribute(UnitAttribute.SpellDpsInc)]
    public override int MDpsInc
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.SpellDpsInc);
            var parameters = new Dictionary<string, double>();
            parameters["int"] = Int;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.SpellDpsInc);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealDps)]
    public override int HDps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
            var res = (weapon?.HDps ?? 0) * 1000;
            res += Spi / 5f * 1000f;
            res = CalculateWithBonuses(res, UnitAttribute.HealDps);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealDpsInc)]
    public override int HDpsInc
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealDpsInc);
            var parameters = new Dictionary<string, double>();
            parameters["spi"] = Spi;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.HealDpsInc);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeAntiMiss)]
    public override float MeleeAccuracy
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeAntiMiss);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            parameters["str"] = Str;
            parameters["heir_level"] = HeirLevel;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MeleeAntiMiss);
            res = (1f - ((Facets / 10f) - res) * (1f / Facets)) * 100f;
            res = ((res + 100f) - Math.Abs((res - 100f))) / 2f;
            res = (Math.Abs(res) + res) / 2f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeCritical)]
    public override float MeleeCritical
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeCritical);
            var parameters = new Dictionary<string, double>();
            parameters["str"] = Str;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MeleeCritical);
            res = res * (1f / Facets) * 100;
            res = res + (MeleeCriticalMul / 10);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeCriticalBonus)]
    public override float MeleeCriticalBonus
    {
        get
        {
            var res = 1500f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.MeleeCriticalBonus);
            return (res - 1000f) / 10f;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeCriticalMul)]
    public override float MeleeCriticalMul
    {
        get
        {
            float res = 0;
            res = (float)CalculateWithBonuses(res, UnitAttribute.MeleeCriticalMul);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedAntiMiss)]
    public override float RangedAccuracy
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.RangedAntiMiss);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            parameters["dex"] = Dex;
            parameters["heir_level"] = HeirLevel;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.RangedAntiMiss);
            res = (1f - ((Facets / 10f) - res) * (1f / Facets)) * 100f;
            res = ((res + 100f) - Math.Abs((res - 100f))) / 2f;
            res = (Math.Abs(res) + res) / 2f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedCritical)]
    public override float RangedCritical
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.RangedCritical);
            var parameters = new Dictionary<string, double>();
            parameters["dex"] = Dex;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.RangedCritical);
            res = res * (1f / Facets) * 100;
            res = res + (RangedCriticalMul / 10);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedCriticalBonus)]
    public override float RangedCriticalBonus
    {
        get
        {
            var res = 1500f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.RangedCriticalBonus);
            return (res - 1000f) / 10f;
        }
    }

    [UnitAttribute(UnitAttribute.RangedCriticalMul)]
    public override float RangedCriticalMul
    {
        get
        {
            float res = 0;
            res = (float)CalculateWithBonuses(res, UnitAttribute.RangedCriticalMul);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellAntiMiss)]
    public override float SpellAccuracy
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.SpellAntiMiss);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            parameters["int"] = Int;
            parameters["spi"] = Spi;
            parameters["heir_level"] = HeirLevel;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.SpellAntiMiss);
            res = (1f - ((Facets / 10f) - res) * (1f / Facets)) * 100f;
            res = ((res + 100f) - Math.Abs((res - 100f))) / 2f;
            res = (Math.Abs(res) + res) / 2f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellCritical)]
    public override float SpellCritical
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.SpellCritical);
            var parameters = new Dictionary<string, double>();
            parameters["int"] = Int;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.SpellCritical);
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellDamageCritical);
            res = res * (1f / Facets) * 100;
            res = res + (SpellCriticalMul / 10);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellCriticalBonus)]
    public override float SpellCriticalBonus
    {
        get
        {
            var res = 1500f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellCriticalBonus);
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellDamageCriticalBonus);
            return (res - 1000f) / 10f;
        }
    }

    [UnitAttribute(UnitAttribute.SpellCriticalMul)]
    public override float SpellCriticalMul
    {
        get
        {
            double res = 0;
            res = CalculateWithBonuses(res, UnitAttribute.SpellCriticalMul);
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellDamageCriticalMul);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealCritical)]
    public override float HealCritical
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealCritical);
            var parameters = new Dictionary<string, double>();
            parameters["spi"] = Spi;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.HealCritical);
            res = res * (1f / Facets) * 100;
            res = res + (HealCriticalMul / 10);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealCriticalBonus)]
    public override float HealCriticalBonus
    {
        get
        {
            var res = 1500f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.HealCriticalBonus);
            return (res - 1000f) / 10f;
        }
    }

    [UnitAttribute(UnitAttribute.HealCriticalMul)]
    public override float HealCriticalMul
    {
        get
        {
            double res = 0;
            res = CalculateWithBonuses(res, UnitAttribute.HealCriticalMul);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.Armor)]
    public override int Armor
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Armor);
            var parameters = new Dictionary<string, double>();
            parameters["sta"] = Sta;
            var res = (int)formula.Evaluate(parameters);
            foreach (var item in Inventory.Equipment.Items)
            {
                switch (item)
                {
                    case Armor armor:
                        res += armor.BaseArmor;
                        break;
                    case Weapon weapon:
                        res += weapon.Armor;
                        break;
                    case Accessory accessory:
                        res += accessory.BaseArmor;
                        break;
                }
            }

            res = (int)CalculateWithBonuses(res, UnitAttribute.Armor);

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.MagicResist)]
    public override int MagicResistance
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MagicResist);
            var parameters = new Dictionary<string, double>();
            parameters["sta"] = Sta;
            var res = (int)formula.Evaluate(parameters);
            foreach (var item in Inventory.Equipment.Items)
            {
                switch (item)
                {
                    case Armor armor:
                        res += armor.BaseMagicResistance;
                        break;
                    case Accessory accessory:
                        res += accessory.BaseMagicResistance;
                        break;
                }
            }

            res = (int)CalculateWithBonuses(res, UnitAttribute.MagicResist);

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.IgnoreArmor)]
    public override int DefensePenetration
    {
        get
        {
            var res = CalculateWithBonuses(0, UnitAttribute.IgnoreArmor);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.MagicPenetration)]
    public override int MagicPenetration
    {
        get
        {
            var res = CalculateWithBonuses(0, UnitAttribute.MagicPenetration);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.BattleResist)]
    public override int BattleResist
    {
        get
        {
            var res = (int)CalculateWithBonuses(0, UnitAttribute.BattleResist);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.BullsEye)]
    public override int BullsEye
    {
        get
        {
            var res = (int)CalculateWithBonuses(0, UnitAttribute.BullsEye);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Flexibility)]
    public override int Flexibility
    {
        get
        {
            var res = (int)CalculateWithBonuses(0, UnitAttribute.Flexibility);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Facets)]
    public override int Facets
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Facet);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.Facets);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Dodge)]
    public override float DodgeRate
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Dodge);
            var parameters = new Dictionary<string, double>();
            parameters["dex"] = Dex;
            parameters["int"] = Int;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.Dodge);
            res = (res * (1f / Facets) * 100f);
            res += CalculateWithBonuses(0f, UnitAttribute.DodgeMul) / 10f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeParry)]
    public override float MeleeParryRate
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeParry);
            var parameters = new Dictionary<string, double>();
            parameters["str"] = Str;
            parameters["spi"] = Spi;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MeleeParry);
            res = (res * (1f / Facets) * 100f);
            res += CalculateWithBonuses(0f, UnitAttribute.MeleeParryMul) / 10f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedParry)]
    public override float RangedParryRate
    {
        get
        {
            //RangedParry Formula == 0
            double res = 0;
            res = CalculateWithBonuses(res, UnitAttribute.RangedParry);
            res = (res * (1f / Facets) * 100f);
            res += CalculateWithBonuses(0f, UnitAttribute.RangedParryMul) / 10f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.Block)]
    public override float BlockRate
    {
        get
        {
            var offhand = Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
            if (offhand != null && offhand.Template is WeaponTemplate template)
            {
                var slotId = (EquipmentItemSlotType)template.HoldableTemplate.SlotTypeId;
                if (slotId != EquipmentItemSlotType.Shield)
                    return 0f;
            }
            else if (offhand == null)
                return 0f;
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Block);
            var parameters = new Dictionary<string, double>();
            parameters["sta"] = Sta;
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.Block);
            res = (res * (1f / Facets) * 100f);
            res += CalculateWithBonuses(0f, UnitAttribute.BlockMul) / 10f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.LungCapacity)]
    public uint LungCapacity
    {
        get => (uint)CalculateWithBonuses(60000, UnitAttribute.LungCapacity);
    }

    [UnitAttribute(UnitAttribute.FallDamageMul)]
    public float FallDamageMul
    {
        get => (float)CalculateWithBonuses(1d, UnitAttribute.FallDamageMul);
    }

    [UnitAttribute(UnitAttribute.LivingPointGain)]
    public float LivingPointGain
    {
        get
        {
            double res = 0.0;
            res = CalculateWithBonuses(res, UnitAttribute.LivingPointGain);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.LivingPointGainMul)]
    public float LivingPointGainMul
    {
        get
        {
            double res = 0.0;
            res = CalculateWithBonuses(res, UnitAttribute.LivingPointGainMul);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.DropRateMul)]
    public float DropRateMul
    {
        get
        {
            double res = 0.0;
            res = CalculateWithBonuses(res, UnitAttribute.DropRateMul);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.LootGoldMul)]
    public float LootGoldMul
    {
        get
        {
            double res = 0.0;
            res = CalculateWithBonuses(res, UnitAttribute.LootGoldMul);
            return (float)res;
        }
    }

    #endregion

    public Character(UnitCustomModelParams modelParams)
    {
        _options = new Dictionary<ushort, string>();
        _hostilePlayers = new ConcurrentDictionary<uint, DateTime>();
        Breath = LungCapacity;
        ModelParams = modelParams;
        Subscribers = new List<IDisposable>();
        ChargeLock = new object();
        FishSchool = new FishSchool(this);
        //Events.OnDisconnect += OnDisconnect;
        //Events.OnCombatStarted += OnEnterCombat;
    }

    public void SetHostileActivity(Character attacker)
    {
        if (_hostilePlayers.ContainsKey(attacker.ObjId))
            _hostilePlayers[attacker.ObjId] = DateTime.UtcNow;
        else
            _hostilePlayers.TryAdd(attacker.ObjId, DateTime.UtcNow);
    }

    public bool IsActivelyHostile(Character target)
    {
        if (_hostilePlayers.TryGetValue(target.ObjId, out var value))
        {
            //Maybe get the time to stay hostile from db?
            return value.AddSeconds(WorldManager.DefaultCombatTimeout) > DateTime.UtcNow;
        }
        return false;
    }

    public void AddExp(int exp, bool shouldAddAbilityExp)
    {
        if (exp == 0)
            return;

        if (exp > 0)
        {
            var totalExp = exp * AppConfiguration.Instance.World.ExpRate;
            exp = (int)totalExp;
        }
        Experience = Math.Min(Experience + exp, ExperienceManager.Instance.GetExpForLevel(55));
        if (shouldAddAbilityExp)
            Abilities.AddActiveExp(exp); // TODO ... or all?
        SendPacket(new SCExpChangedPacket(ObjId, exp, shouldAddAbilityExp));
        CheckLevelUp();

        //Quests.OnLevelUp(); // TODO added for quest Id=5967
        // инициируем событие
        //Task.Run(() => QuestManager.Instance.DoOnLevelUpEvents(Connection.ActiveChar));
        if (Connection != null)
        {
            QuestManager.Instance.DoOnLevelUpEvents(Connection.ActiveChar);
        }
    }

    public void CheckLevelUp()
    {
        var needExp = ExperienceManager.Instance.GetExpForLevel((byte)(Level + 1));
        var change = false;
        while (Experience >= needExp)
        {
            change = true;
            Level++;
            needExp = ExperienceManager.Instance.GetExpForLevel((byte)(Level + 1));
            Expedition?.OnCharacterRefresh(this);
        }

        if (change)
        {
            BroadcastPacket(new SCLevelChangedPacket(ObjId, Level), true);
            //StartRegen();
            ResidentManager.Instance.AddResidenMemberInfo(this);
        }
    }

    public void CheckExp()
    {
        var needExp = ExperienceManager.Instance.GetExpForLevel(Level);
        if (Experience < needExp)
            Experience = needExp;
        needExp = ExperienceManager.Instance.GetExpForLevel((byte)(Level + 1));
        while (Experience >= needExp)
        {
            Level++;
            needExp = ExperienceManager.Instance.GetExpForLevel((byte)(Level + 1));
            Expedition?.OnCharacterRefresh(this);
        }
    }

    public bool ChangeMoney(SlotType moneylocation, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney) => ChangeMoney(SlotType.None, moneylocation, amount, itemTaskType);

    public bool ChangeMoney(SlotType typeFrom, SlotType typeTo, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        var itemTasks = new List<ItemTask>();
        switch (typeFrom)
        {
            case SlotType.Inventory:
                if (amount > Money)
                {
                    SendErrorMessage(ErrorMessageType.NotEnoughMoney);
                    return false;
                }
                Money -= amount;
                itemTasks.Add(new MoneyChange(-amount));
                break;
            case SlotType.Bank:
                if (amount > Money2)
                {
                    SendErrorMessage(ErrorMessageType.NotEnoughMoney);
                    return false;
                }
                Money2 -= amount;
                itemTasks.Add(new MoneyChangeBank(-amount));
                break;
        }
        switch (typeTo)
        {
            case SlotType.Inventory:
                Money += amount;
                itemTasks.Add(new MoneyChange(amount));
                break;
            case SlotType.Bank:
                Money2 += amount;
                itemTasks.Add(new MoneyChangeBank(amount));
                break;
        }
        SendPacket(new SCItemTaskSuccessPacket(itemTaskType, itemTasks, new List<ulong>()));
        return true;
    }

    public bool AddMoney(SlotType moneyLocation, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        if (amount < 0)
            return false;
        return ChangeMoney(SlotType.None, moneyLocation, amount, itemTaskType);
    }

    public bool SubtractMoney(SlotType moneyLocation, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        if (amount < 0)
            return false;
        return ChangeMoney(SlotType.None, moneyLocation, -amount, itemTaskType);
    }

    public void ChangeLabor(short change, int actabilityId)
    {
        var actabilityChange = 0;
        byte actabilityStep = 0;
        if (actabilityId > 0)
        {
            actabilityChange = Math.Abs(change);
            actabilityStep = Actability.Actabilities[(uint)actabilityId].Step;
            actabilityChange = Actability.AddPoint((uint)actabilityId, actabilityChange);
        }

        // Only grant xp if consuming labor
        if (change < 0)
        {
            var parameters = new Dictionary<string, double>();
            parameters.Add("labor_power", -change);
            parameters.Add("pc_level", Level);
            var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.ExpByLaborPower);
            var xpToAdd = (int)formula.Evaluate(parameters);
            AddExp(xpToAdd, true);
        }

        LaborPower += change;
        SendPacket(new SCCharacterLaborPowerChangedPacket(change, actabilityId, actabilityChange, actabilityStep));
    }

    public void ChangeGamePoints(GamePointKind kind, int change)
    {
        switch (kind)
        {
            case GamePointKind.Honor:
                HonorPoint += change;
                break;
            case GamePointKind.Vocation:
                var vocAdd = GetAttribute(UnitAttribute.LivingPointGain, 0f);
                change = (int)Math.Round(change + vocAdd);
                var vocMul = GetAttribute(UnitAttribute.LivingPointGainMul, 0f) + 100f;
                change = (int)Math.Round(change * (vocMul / 100f));
                VocationPoint += change;
                break;
            default:
                Logger.Error($"ChangeGamePoints - Unknown Game Point Type {kind}");
                return;
        }
        int[,] points = { { (int)kind, change } };

        SendPacket(new SCGamePointChangedPacket(points));
    }

    public override int GetAbLevel(AbilityType type)
    {
        if (type == AbilityType.General) return Level;
        return ExperienceManager.Instance.GetLevelFromExp(Abilities.Abilities[type].Exp);
    }

    public void ResetSkillCooldown(uint skillId, bool gcd)
    {
        SendPacket(new SCSkillCooldownResetPacket(this, skillId, 0, gcd));
    }

    public void ResetAllSkillCooldowns(bool triggerGcd)
    {
        const uint playerSkillsTag = 378;
        var skillIds = SkillManager.Instance.GetSkillsByTag(playerSkillsTag);

        var packets = new CompressedGamePackets();
        foreach (var skillId in skillIds)
        {
            packets.AddPacket(new SCSkillCooldownResetPacket(this, skillId, 0, triggerGcd));
        }
        SendPacket(packets);
    }

    public void SetPirate(bool pirate)
    {
        // TODO : If castle owner -> Nope
        var defaultFactionId = CharacterManager.Instance.GetTemplate(Race, Gender).FactionId;

        var newFaction = pirate ? FactionsEnum.Pirate : defaultFactionId;
        BroadcastPacket(new SCUnitFactionChangedPacket(ObjId, Name, Faction.Id, newFaction, false), true);
        Faction = FactionManager.Instance.GetFaction(newFaction);
        HousingManager.Instance.UpdateOwnedHousingFaction(Id, newFaction);
        // TODO : Teleport to Growlgate
        // TODO : Leave guild
    }

    public override void SetPosition(float x, float y, float z, float rotationX, float rotationY, float rotationZ)
    {
        var moved = !Transform.Local.Position.X.Equals(x) || !Transform.Local.Position.Y.Equals(y) || !Transform.Local.Position.Z.Equals(z);
        var lastZoneKey = Transform.ZoneId;
        //Connection.ActiveChar.SendMessage("Move Old Pos: {0}", Transform.ToString());

        base.SetPosition(x, y, z, rotationX, rotationY, rotationZ);

        var worldDrownThreshold = WorldManager.Instance.GetWorld(Transform.WorldId)?.OceanLevel - 2f ?? 98f;
        if (!IsUnderWater && Transform.World.Position.Z < worldDrownThreshold)
            IsUnderWater = true;
        else if (IsUnderWater && Transform.World.Position.Z > worldDrownThreshold)
            IsUnderWater = false;

        // Connection.ActiveChar.SendMessage("Move New Pos: {0}", Transform.ToString());

        if (!moved)
            return;

        Buffs.TriggerRemoveOn(BuffRemoveOn.Move);

        // Update the party member position on the map
        // TODO: Check the format of the send packet, as it doesn't seem to be correct
        // TODO: Somehow make sure that players in instances don't show on the main world map 
        if (InParty)
            TeamManager.Instance.UpdatePosition(Id);

        // Check if zone changed
        if (Transform.ZoneId == lastZoneKey)
            return;
        OnZoneChange(lastZoneKey, Transform.ZoneId);
    }

    private CancellationTokenSource _unreleasedZoneTransportedOut;

    public override void OnZoneChange(uint lastZoneKey, uint newZoneKey)
    {
        base.OnZoneChange(lastZoneKey, newZoneKey); // Unit

        var newZone = ZoneManager.Instance.GetZoneByKey(newZoneKey);
        var newZoneGroupId = (short)(newZone?.GroupId ?? 0);

        if (newZone != null)
        {
            Expedition?.OnCharacterRefresh(this);
        }

        if (newZone is { Closed: false })
        {
            if (_unreleasedZoneTransportedOut != null)
            {
                _unreleasedZoneTransportedOut.Cancel();
                _unreleasedZoneTransportedOut = null;
            }

            return;
        }

        // Send extra info to player if we are still in a real but unreleased zone (not null), this is not retail behaviour!
        if (newZone != null)
            SendMessage(ChatType.System, $"You have entered a closed zone ({newZone.ZoneKey} - {newZone.Name})!\nPlease leave immediately!", Color.Red);
        
        var characterAccessLevel = CharacterManager.Instance.GetEffectiveAccessLevel(this);
        if (characterAccessLevel < 100)
        {
            // Do forbidden zone code handling
            if (_unreleasedZoneTransportedOut != null)
            {
                return;
            }

            _unreleasedZoneTransportedOut = new CancellationTokenSource();
            Task.Run(async () =>
            {
                // Stay for a maximum of 10 seconds
                for (var i = 0; i < 5; i++)
                {
                    // sendErrorMsg
                    SendErrorMessage(ErrorMessageType.ClosedZone,0,false);
                    await Task.Delay(2 * 1000,_unreleasedZoneTransportedOut.Token);
                }
                ForceDismount();
                MateManager.Instance.RemoveAndDespawnAllActiveOwnedMates(this);
                await Task.Delay(200);
                var portal = PortalManager.Instance.GetClosestReturnPortal(Connection.ActiveChar);
                // force transported out
                Connection.ActiveChar.BroadcastPacket(
                    new SCCharacterResurrectedPacket(
                        Connection.ActiveChar.ObjId,
                        portal.X,
                        portal.Y,
                        portal.Z,
                        portal.ZRot
                    ),
                    true
                );
            
            }, _unreleasedZoneTransportedOut.Token);
        }
    }

    public override int DoFallDamage(ushort fallVel)
    {
        if (CharacterManager.Instance.GetEffectiveAccessLevel(this) >= AppConfiguration.Instance.World.IgnoreFallDamageAccessLevel)
        {
            Logger.Debug($"{Name} negated FallDamage because of IgnoreFallDamageAccessLevel settings");
            return 0; // GM & Admin take 0 damage from falling
        }
        var fallDamage = base.DoFallDamage(fallVel);
        Logger.Trace($"FallDamage: {Name} - Vel {fallVel} DmgPerc: {(int)((fallVel - 8600) / 150f)}, Damage {fallDamage}");
        return fallDamage;
    }

    /// <summary>
    /// ItemUse - is used to work the quests
    /// </summary>
    /// <param name="id">item.id</param>
    public void ItemUse(ulong id)
    {
        var item = Inventory.GetItemById(id);
        if (item is { Count: > 0 })
        {
            // Trigger event
            Events?.OnItemUse(this, new OnItemUseArgs
            {
                ItemId = item.TemplateId
            });
        }
    }

    /// <summary>
    /// ItemUse - is used to work the quests
    /// </summary>
    /// <param name="item"></param>
    public void ItemUse(Item item)
    {
        if (item is not null)
        {
            // Trigger event
            Events?.OnItemUse(this, new OnItemUseArgs
            {
                ItemId = item.TemplateId
            });
        }
    }

    /// <summary>
    /// Trigger OnItemUse using a item template
    /// </summary>
    /// <param name="itemTemplate"></param>
    public void ItemUseByTemplate(uint itemTemplate)
    {
        if (itemTemplate > 0)
        {
            // Trigger event
            Events?.OnItemUse(this, new OnItemUseArgs
            {
                ItemId = itemTemplate
            });
        }
    }


    public void SetAction(byte slot, ActionSlotType type, uint actionId)
    {
        Slots[slot].Type = type;
        Slots[slot].ActionId = actionId;
    }

    public void SetAction(byte slot, ActionSlotType type, ulong itemId)
    {
        Slots[slot].Type = type;
        Slots[slot].ActionId = itemId;
    }

    public void SetOption(ushort key, string value)
    {
        _options[key] = value;
    }

    public string GetOption(ushort key)
    {
        if (_options.TryGetValue(key, out var option))
            return option;
        return "";
    }

    public void PushSubscriber(IDisposable disposable)
    {
        Subscribers.Add(disposable);
    }

    public void SendOption(ushort key)
    {
        Connection.SendPacket(new SCResponseUIDataPacket(Id, key, GetOption(key)));
    }

    /// <summary>
    /// Sends a chat message
    /// </summary>
    /// <param name="type">Chat Type to use</param>
    /// <param name="message">The actual text</param>
    /// <param name="color">If set, adds a color tags to the beginning and the end of the text</param>
    public void SendMessage(ChatType type, string message, Color? color = null)
    {
        if (color != null)
            message = $"|c{color.Value.A:X2}{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}{message}|r";
        SendPacket(new SCChatMessagePacket(type, message));
    }

    public void SendMessage(string message) => SendMessage(ChatType.System, message, null);

    public void SendErrorMessage(ErrorMessageType errorMsgType, uint type = 0, bool isNotify = true)
    {
        SendPacket(new SCErrorMsgPacket(errorMsgType, type, isNotify));
    }

    public void SendErrorMessage(ErrorMessageType errorMsgType1, ErrorMessageType errorMsgType2, uint type = 0, bool isNotify = true)
    {
        SendPacket(new SCErrorMsgPacket(errorMsgType1, errorMsgType2, type, isNotify));
    }

    public static Character Load(uint characterId, uint accountId)
    {
        using (var connection = MySQL.CreateConnection())
            return Load(connection, characterId, accountId);
    }

    public static Character Load(uint characterId)
    {
        using (var connection = MySQL.CreateConnection())
            return Load(connection, characterId);
    }

    public uint Breath { get; set; }

    public bool IsDrowning
    {
        get { return (Breath <= 0); }
    }

    public TimeSpan OnlineTime { get; set; } = TimeSpan.Zero;

    public override void ReduceCurrentHp(BaseUnit attacker, int value, KillReason killReason = KillReason.Damage)
    {
        if (AppConfiguration.Instance.World.GodMode)
        {
            Logger.Debug($"{Name}'s damage disabled because of GodMode flag (normal damage: {value})");
            return; // GodMode On : take no damage at all
        }

        if (IsInDuel)
        {
            Hp = Math.Max(Hp - value, 1); // we don't let you die during a duel
            value = 0;
        }

        base.ReduceCurrentHp(attacker, value, killReason);
    }

    public void DoChangeBreath()
    {
        if (IsDrowning)
        {
            var damageAmount = MaxHp * .1;
            ReduceCurrentHp(this, (int)damageAmount);
            SendPacket(new SCEnvDamagePacket(EnvSource.Drowning, ObjId, (uint)damageAmount));
        }
        else
        {
            Breath -= 1000; //1 second
            SendPacket(new SCSetBreathPacket(Breath));
        }
    }

    public void DoRepair(List<Item> items)
    {
        var tasks = new List<ItemTask>();
        int repairCost = 0;

        foreach (var item in items)
        {
            if (item == null)
                continue;

            if (!Inventory.Bag.Items.Contains(item) && !Equipment.Items.Contains(item))
            {
                Logger.Warn("Attempting to repair an item that isn't in your inventory or equipment, Item: {0}", item.Id);
                continue;
            }

            if (!(item is EquipItem equipItem && item.Template is EquipItemTemplate))
            {
                Logger.Warn("Attempting to repair a non-equipment item, Item: {0}", item.Id);
                continue;
            }

            if (equipItem.Durability >= equipItem.MaxDurability)
            {
                Logger.Warn("Attempting to repair an item that has max durability, Item: {0}", item.Id);
                continue;
            }

#pragma warning disable CA1508 // Avoid dead conditional code
            if (CurrentInteractionObject is null || !(CurrentInteractionObject is Npc npc))
                continue;
#pragma warning restore CA1508 // Avoid dead conditional code

            if (!npc.Template.Blacksmith)
            {
                Logger.Warn($"Attempting to repair an item while not at a blacksmith, Item={item.Id}, NPC={npc}");
                continue;
            }

            var dist = MathUtil.CalculateDistance(Transform.World.Position, npc.Transform.World.Position);

            if (dist > 5f)
            {
                SendErrorMessage(ErrorMessageType.TooFarAway);
                continue;
            }

            var currentRepairCost = equipItem.RepairCost;

            if (Money < currentRepairCost)
            {
                Logger.Warn($"Not enough money to repair, Item: {item.Id}, Money: {Money}, RepairCost: {currentRepairCost}");
                continue;
            }

            equipItem.Durability = equipItem.MaxDurability;
            equipItem.IsDirty = true;
            repairCost += currentRepairCost;
            // добавил 4 байта перед Durability для нормальной работы починки предметов 
            tasks.Add(new ItemUpdateRepair(item));
        }

        if (repairCost > 0)
        {
            //ChangeMoney(SlotType.Inventory, -repairCost);
            tasks.Add(new MoneyChange(-repairCost));
        }

        Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Repair, tasks, []));
    }

    public override void Regenerate()
    {
        if (IsDead || !NeedsRegen || IsDrowning)
        {
            return;
        }

        var oldHp = Hp;

        if (IsInBattle)
        {
            Hp += PersistentHpRegen;
        }
        else
        {
            Hp += HpRegen;
        }

        if (IsInPostCast)
        {
            Mp += PersistentMpRegen;
        }
        else
        {
            Mp += MpRegen;
        }

        Hp = Math.Min(Hp, MaxHp);
        Mp = Math.Min(Mp, MaxMp);
        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Mp, HighAbilityRsc), true);
        PostUpdateCurrentHp(this, oldHp, Hp, KillReason.Unknown);
    }

    /// <summary>
    /// Forcibly remove character from any mount or vehicle they might be riding,
    /// useful for calling before any kind of teleport function 
    /// </summary>
    /// <returns>Returns True is any dismounting happened by this function</returns>
    public bool ForceDismount(AttachUnitReason reason = AttachUnitReason.PrefabChanged)
    {
        var res = false;
        // Force dismount Mates (mounts)
        var isOnMount = MateManager.Instance.GetIsMounted(ObjId, out var attachedRiderPoint);
        if (isOnMount != null)
        {
            MateManager.Instance.UnMountMate(Connection, isOnMount.TlId, attachedRiderPoint, reason);
            res = true;
        }
        // Force remove from slaves
        var isOnSlave = SlaveManager.Instance.GetIsMounted(ObjId, out var attachedDriverPoint);
        if (isOnSlave != null)
        {
            SlaveManager.Instance.UnbindSlave(this, isOnSlave.TlId, reason);
            res = true;
        }
        // Unbind from any parent
        Transform.DetachAll();
        return res;
    }

    public bool ForceDismountAndDespawn(AttachUnitReason reason = AttachUnitReason.PrefabChanged, int timeToDespawn = 1000 * 60 * 10)
    {
        var res = ForceDismount();

        var mySlave = SlaveManager.Instance.GetSlaveByOwnerObjId(Connection.ActiveChar.ObjId);
        if (mySlave != null)
        {
            // run the task to turn off the transport after timeToDespawn minutes
            mySlave.CancelTokenSource = new CancellationTokenSource();
            var token = mySlave.CancelTokenSource.Token;
            mySlave.LeaveTask = new Task(() =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(timeToDespawn)); // 10 minutes
                if (token.IsCancellationRequested)
                    return;
                SlaveManager.Instance.RemoveAndDespawnAllActiveOwnedSlaves(this);
            }, token);
            mySlave.LeaveTask.Start();
        }

        return res;
    }

    /// <summary>
    /// ForceDismountAndDespawn - deleting Mirage's test transport
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="timeToDespawn"></param>
    /// <returns></returns>
    public bool ForceDismountAndDespawn(Slave slave, int timeToDespawn = 100)
    {
        var res = ForceDismount();

        if (slave != null)
        {
            // run the task to turn off the transport after timeToDespawn minutes
            slave.CancelTokenSource = new CancellationTokenSource();
            var token = slave.CancelTokenSource.Token;
            slave.LeaveTask = new Task(() =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(timeToDespawn));
                if (token.IsCancellationRequested)
                    return;
                SlaveManager.Instance.RemoveAndDespawnTestSlave(this, slave.ObjId);
            }, token);
            slave.LeaveTask.Start();
        }

        return res;
    }

    public void RemoveAndDespawnActiveOwnedMatesSlaves()
    {
        // Despawn and unmount everybody from owned Mates
        MateManager.Instance.RemoveAndDespawnAllActiveOwnedMates(this);
        ForceDismountAndDespawn();
    }

    #region Database

    public static Character Load(MySqlConnection connection, uint characterId, ulong accountId)
    {
        var accountDetails = AccountManager.Instance.GetAccountDetails(accountId);
        Character character = null;
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.CommandText = "SELECT * FROM characters WHERE `id` = @id AND `account_id` = @account_id and `deleted`=0";
            command.Parameters.AddWithValue("@id", characterId);
            command.Parameters.AddWithValue("@account_id", accountId);
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    var stream = (PacketStream)(byte[])reader.GetValue("unit_model_params");
                    var modelParams = new UnitCustomModelParams();
                    modelParams.Read(stream);

                    character = new Character(modelParams);
                    character.AccountId = accountId;
                    character.Id = reader.GetUInt32("id");
                    character.Name = reader.GetString("name");
                    character.AccessLevel = reader.GetInt32("access_level");
                    character.Race = (Race)reader.GetByte("race");
                    character.Gender = (Gender)reader.GetByte("gender");
                    character.Level = reader.GetByte("level");
                    character.Experience = reader.GetInt32("experience");
                    character.RecoverableExp = reader.GetInt32("recoverable_exp");
                    character.Hp = reader.GetInt32("hp");
                    character.Mp = reader.GetInt32("mp");
                    // character.LaborPower = reader.GetInt16("labor_power");
                    // character.LaborPowerModified = reader.GetDateTime("labor_power_modified");
                    character.InitializeLaborCache(accountDetails.Labor, accountDetails.LastUpdated);
                    character.ConsumedLaborPower = reader.GetInt32("consumed_lp");
                    character.Ability1 = (AbilityType)reader.GetByte("ability1");
                    character.Ability2 = (AbilityType)reader.GetByte("ability2");
                    character.Ability3 = (AbilityType)reader.GetByte("ability3");
                    character.Transform = new Transform(character, null,
                        reader.GetUInt32("world_id"), reader.GetUInt32("zone_id"), WorldManager.DefaultInstanceId,
                        reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z"),
                        reader.GetFloat("yaw"), reader.GetFloat("pitch"), reader.GetFloat("roll")
                        );
                    character.Faction = FactionManager.Instance.GetFaction((FactionsEnum)reader.GetUInt32("faction_id"));
                    character.FactionName = reader.GetString("faction_name");
                    character.Expedition = ExpeditionManager.Instance.GetExpedition((FactionsEnum)reader.GetUInt32("expedition_id"));
                    character.Family = reader.GetUInt32("family");
                    character.DeadCount = reader.GetInt16("dead_count");
                    character.DeadTime = reader.GetDateTime("dead_time");
                    character.RezWaitDuration = reader.GetInt32("rez_wait_duration");
                    character.RezTime = reader.GetDateTime("rez_time");
                    character.RezPenaltyDuration = reader.GetInt32("rez_penalty_duration");
                    character.LeaveTime = reader.GetDateTime("leave_time");
                    character.Money = reader.GetInt64("money");
                    character.Money2 = reader.GetInt64("money2");
                    character.HonorPoint = reader.GetInt32("honor_point");
                    character.VocationPoint = reader.GetInt32("vocation_point");
                    character.CrimePoint = reader.GetInt16("crime_point");
                    character.CrimeRecord = reader.GetInt32("crime_record");
                    character.JuryPoint = reader.GetInt32("jury_point");
                    character.HostileFactionKills = reader.GetUInt32("hostile_faction_kills");
                    character.HonorGainedInCombat = reader.GetUInt32("pvp_honor");
                    character.TransferRequestTime = reader.GetDateTime("transfer_request_time");
                    character.DeleteRequestTime = reader.GetDateTime("delete_request_time");
                    character.DeleteTime = reader.GetDateTime("delete_time");
                    character.AutoUseAAPoint = reader.GetBoolean("auto_use_aapoint");
                    character.PrevPoint = reader.GetInt32("prev_point");
                    character.Point = reader.GetInt32("point");
                    character.Gift = reader.GetInt32("gift");
                    character.NumInventorySlots = reader.GetByte("num_inv_slot");
                    character.NumBankSlots = reader.GetInt16("num_bank_slot");
                    character.ExpandedExpert = reader.GetByte("expanded_expert");
                    character.Created = reader.GetDateTime("created_at");
                    character.Updated = reader.GetDateTime("updated_at");
                    character.ReturnDistrictId = reader.GetUInt32("return_district");
                    character.OnlineTime = TimeSpan.FromSeconds(reader.GetUInt32("online_time"));

                    character.Inventory = new Inventory(character);

                    var slotsBlob = (PacketStream)((byte[])reader.GetValue("slots"));
                    character.LoadActionSlots(slotsBlob);

                    character.BmPoint = AccountManager.Instance.GetAccountDetails(character.AccountId).Loyalty;

                    if (character.Hp > character.MaxHp)
                        character.Hp = character.MaxHp;
                    if (character.Mp > character.MaxMp)
                        character.Mp = character.MaxMp;
                    character.CheckExp();
                    character.PostUpdateCurrentHp(character, 0, character.Hp, KillReason.Unknown);
                }
            }
        }

        if (character == null)
            return null;

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.CommandText = "SELECT * FROM `options` WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", characterId);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var key = reader.GetUInt16("key");
                    var value = reader.GetString("value");
                    character.SetOption(key, value);
                }
            }
        }

        return character;
    }

    public static Character Load(MySqlConnection connection, uint characterId)
    {
        Character character = null;
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.CommandText = "SELECT * FROM characters WHERE `id` = @id and `deleted`=0";
            command.Parameters.AddWithValue("@id", characterId);
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    var stream = (PacketStream)(byte[])reader.GetValue("unit_model_params");
                    var modelParams = new UnitCustomModelParams();
                    modelParams.Read(stream);

                    character = new Character(modelParams);
                    character.Id = reader.GetUInt32("id");
                    character.AccountId = reader.GetUInt32("account_id");

                    var accountDetails = AccountManager.Instance.GetAccountDetails(character.AccountId);

                    character.Name = reader.GetString("name");
                    character.AccessLevel = reader.GetInt32("access_level");
                    character.Race = (Race)reader.GetByte("race");
                    character.Gender = (Gender)reader.GetByte("gender");
                    character.Level = reader.GetByte("level");
                    character.Experience = reader.GetInt32("experience");
                    character.RecoverableExp = reader.GetInt32("recoverable_exp");
                    character.Hp = reader.GetInt32("hp");
                    character.Mp = reader.GetInt32("mp");
                    character.InitializeLaborCache(accountDetails.Labor, accountDetails.LastUpdated);
                    // character.LaborPower = reader.GetInt16("labor_power");
                    // character.LaborPowerModified = reader.GetDateTime("labor_power_modified");
                    character.ConsumedLaborPower = reader.GetInt32("consumed_lp");
                    character.Ability1 = (AbilityType)reader.GetByte("ability1");
                    character.Ability2 = (AbilityType)reader.GetByte("ability2");
                    character.Ability3 = (AbilityType)reader.GetByte("ability3");
                    character.Transform = new Transform(character, null,
                        reader.GetUInt32("world_id"), reader.GetUInt32("zone_id"), WorldManager.DefaultInstanceId,
                        reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z"),
                        reader.GetFloat("yaw"), reader.GetFloat("pitch"), reader.GetFloat("roll")
                        );
                    character.Faction = FactionManager.Instance.GetFaction((FactionsEnum)reader.GetUInt32("faction_id"));
                    character.FactionName = reader.GetString("faction_name");
                    character.Expedition = ExpeditionManager.Instance.GetExpedition((FactionsEnum)reader.GetUInt32("expedition_id"));
                    character.Family = reader.GetUInt32("family");
                    character.DeadCount = reader.GetInt16("dead_count");
                    character.DeadTime = reader.GetDateTime("dead_time");
                    character.RezWaitDuration = reader.GetInt32("rez_wait_duration");
                    character.RezTime = reader.GetDateTime("rez_time");
                    character.RezPenaltyDuration = reader.GetInt32("rez_penalty_duration");
                    character.LeaveTime = reader.GetDateTime("leave_time");
                    character.Money = reader.GetInt64("money");
                    character.Money2 = reader.GetInt64("money2");
                    character.HonorPoint = reader.GetInt32("honor_point");
                    character.VocationPoint = reader.GetInt32("vocation_point");
                    character.CrimePoint = reader.GetInt16("crime_point");
                    character.CrimeRecord = reader.GetInt32("crime_record");
                    character.JuryPoint = reader.GetInt16("jury_point");
                    character.HostileFactionKills = reader.GetUInt32("hostile_faction_kills");
                    character.HonorGainedInCombat = reader.GetUInt32("pvp_honor");
                    character.TransferRequestTime = reader.GetDateTime("transfer_request_time");
                    character.DeleteRequestTime = reader.GetDateTime("delete_request_time");
                    character.DeleteTime = reader.GetDateTime("delete_time");
                    // character.BmPoint = reader.GetInt32("bm_point");
                    character.AutoUseAAPoint = reader.GetBoolean("auto_use_aapoint");
                    character.PrevPoint = reader.GetInt32("prev_point");
                    character.Point = reader.GetInt32("point");
                    character.Gift = reader.GetInt32("gift");
                    character.NumInventorySlots = reader.GetByte("num_inv_slot");
                    character.NumBankSlots = reader.GetInt16("num_bank_slot");
                    character.ExpandedExpert = reader.GetByte("expanded_expert");
                    character.Created = reader.GetDateTime("created_at");
                    character.Updated = reader.GetDateTime("updated_at");
                    character.ReturnDistrictId = reader.GetUInt32("return_district");
                    character.OnlineTime = TimeSpan.FromSeconds(reader.GetUInt32("online_time"));

                    character.Inventory = new Inventory(character);

                    var slotsBlob = (PacketStream)((byte[])reader.GetValue("slots"));
                    character.LoadActionSlots(slotsBlob);

                    character.BmPoint = AccountManager.Instance.GetAccountDetails(character.AccountId).Loyalty;

                    if (character.Hp > character.MaxHp)
                        character.Hp = character.MaxHp;
                    if (character.Mp > character.MaxMp)
                        character.Mp = character.MaxMp;
                    character.CheckExp();
                    character.PostUpdateCurrentHp(character, 0, character.Hp, KillReason.Unknown);
                }
            }
        }

        if (character == null)
            return null;

        return character;
    }

    private void LoadActionSlots(PacketStream slotsBlob)
    {
        if (Slots == null)
        {
            Slots = new ActionSlot[MaxActionSlots];
            for (var i = 0; i < Slots.Length; i++)
                Slots[i] = new ActionSlot();
        }

        foreach (var slot in Slots)
        {
            slot.Type = (ActionSlotType)slotsBlob.ReadByte();
            switch (slot.Type)
            {
                case ActionSlotType.None:
                    {
                        break;
                    }
                case ActionSlotType.ItemType:
                case ActionSlotType.Spell:
                case ActionSlotType.RidePetSpell:
                    {
                        slot.ActionId = slotsBlob.ReadUInt32();
                        break;
                    }
                case ActionSlotType.ItemId:
                    {
                        slot.ActionId = slotsBlob.ReadUInt64(); // itemId
                        break;
                    }
                default:
                    {
                        Logger.Error("LoadActionSlots, Unknown ActionSlotType!");
                        break;
                    }
            }
        }
    }

    private void LoadActionSlots(MySqlConnection connection)
    {
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.CommandText = "SELECT slots FROM `characters` WHERE `id` = @id AND `account_id` = @account_id";
                command.Parameters.AddWithValue("@id", Id);
                command.Parameters.AddWithValue("@account_id", AccountId);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var slotsBlob = (PacketStream)((byte[])reader.GetValue("slots"));
                        LoadActionSlots(slotsBlob);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"LoadActionSlots, error while loading for character {Id}, {ex.Message}");
        }
    }

    private byte[] GetActionSlotsAsBlob()
    {
        var slotsBlob = new PacketStream();
        foreach (var slot in Slots)
        {
            slotsBlob.Write((byte)slot.Type);

            switch (slot.Type)
            {
                case ActionSlotType.None:
                    {
                        break;
                    }
                case ActionSlotType.ItemType:
                case ActionSlotType.Spell:
                case ActionSlotType.RidePetSpell:
                    {
                        slotsBlob.Write((uint)slot.ActionId);
                        break;
                    }
                case ActionSlotType.ItemId:
                    {
                        slotsBlob.Write(slot.ActionId); // itemId
                        break;
                    }
                default:
                    {
                        Logger.Error("GetActionSlotsAsBlob, Unknown ActionSlotType!");
                        break;
                    }
            }
        }
        return slotsBlob.GetBytes();
    }

    public void Load()
    {
        var template = CharacterManager.Instance.GetTemplate(Race, Gender);
        ModelId = template.ModelId;
        BuyBackItems = new ItemContainer(Id, SlotType.None, false, this);
        Slots = new ActionSlot[MaxActionSlots];
        for (var i = 0; i < Slots.Length; i++)
            Slots[i] = new ActionSlot();

        Craft = new CharacterCraft(this);
        Procs = new UnitProcs(this);
        LocalPingPosition = new TeamPingPos();

        using (var connection = MySQL.CreateConnection())
        {
            // Inventory.Load(connection);
            Abilities = new CharacterAbilities(this);
            Abilities.Load(connection);
            Actability = new CharacterActability(this);
            Actability.Load(connection);
            Skills = new CharacterSkills(this);
            Skills.Load(connection);
            Appellations = new CharacterAppellations(this);
            Appellations.Load(connection);
            Portals = new CharacterPortals(this);
            Portals.Load(connection);
            Friends = new CharacterFriends(this);
            Friends.Load(connection);
            Blocked = new CharacterBlocked(this);
            Blocked.Load(connection);
            Quests = new CharacterQuests(this);
            Quests.Load(connection);
            Quests.CheckDailyResetAtLogin();
            Mates = new CharacterMates(this);
            Mates.Load(connection);
            Attendances = new CharacterAttendances(this);
            Attendances.Load(connection);

            LoadActionSlots(connection);
        }

        Mails = new CharacterMails(this);
        MailManager.Instance.GetCurrentMailList(this); //Doesn't need a connection, but does need to load after the inventory
        // Update sync housing factions on login
        HousingManager.Instance.UpdateOwnedHousingFaction(Id, Faction.Id);
    }

    public bool SaveDirectlyToDatabase()
    {
        // Try to save New Character
        var saved = false;
        using (var sqlConnection = MySQL.CreateConnection())
        {
            using (var transaction = sqlConnection.BeginTransaction())
            {
                try
                {
                    saved = Save(sqlConnection, transaction);
                    transaction.Commit();
                }
                catch (Exception e)
                {
                    saved = false;
                    Logger.Error(e, "Character save failed for {0} - {1}\n", Id, Name);
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception eRollback)
                    {
                        // Really failed here
                        Logger.Fatal(eRollback, "Character save rollback failed for {0} - {1}\n", Id, Name);
                    }
                }
            }
        }
        return saved;
    }

    public bool Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        bool result;
        try
        {
            var unitModelParams = ModelParams.Write(new PacketStream()).GetBytes();

            Updated = DateTime.UtcNow; // обновим время записи информации

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                // ----
                command.CommandText =
                    "REPLACE INTO `characters` " +
                    "(`id`,`account_id`,`name`,`access_level`,`race`,`gender`,`unit_model_params`,`level`,`experience`,`recoverable_exp`," +
                    "`hp`,`mp`,`consumed_lp`,`ability1`,`ability2`,`ability3`," +
                    "`world_id`,`zone_id`,`x`,`y`,`z`,`roll`,`pitch`,`yaw`," +
                    "`faction_id`,`faction_name`,`expedition_id`,`family`,`dead_count`,`dead_time`,`rez_wait_duration`,`rez_time`,`rez_penalty_duration`,`leave_time`," +
                    "`money`,`money2`,`honor_point`,`vocation_point`,`crime_point`,`crime_record`,`jury_point`," +
                    "`delete_request_time`,`transfer_request_time`,`delete_time`,`auto_use_aapoint`,`prev_point`,`point`,`gift`," +
                    "`num_inv_slot`,`num_bank_slot`,`expanded_expert`,`slots`,`created_at`,`updated_at`,`return_district`,`online_time`" +
                    ") VALUES (" +
                    "@id,@account_id,@name,@access_level,@race,@gender,@unit_model_params,@level,@experience,@recoverable_exp," +
                    "@hp,@mp,@consumed_lp,@ability1,@ability2,@ability3," +
                    "@world_id,@zone_id,@x,@y,@z,@yaw,@pitch,@roll," +
                    "@faction_id,@faction_name,@expedition_id,@family,@dead_count,@dead_time,@rez_wait_duration,@rez_time,@rez_penalty_duration,@leave_time," +
                    "@money,@money2,@honor_point,@vocation_point,@crime_point,@crime_record,@jury_point," +
                    "@delete_request_time,@transfer_request_time,@delete_time,@auto_use_aapoint,@prev_point,@point,@gift," +
                    "@num_inv_slot,@num_bank_slot,@expanded_expert,@slots,@created_at,@updated_at,@return_district,@online_time)";

                command.Parameters.AddWithValue("@id", Id);
                command.Parameters.AddWithValue("@account_id", AccountId);
                command.Parameters.AddWithValue("@name", Name);
                command.Parameters.AddWithValue("@access_level", AccessLevel);
                command.Parameters.AddWithValue("@race", (byte)Race);
                command.Parameters.AddWithValue("@gender", (byte)Gender);
                command.Parameters.AddWithValue("@unit_model_params", unitModelParams);
                command.Parameters.AddWithValue("@level", Level);
                command.Parameters.AddWithValue("@experience", Experience);
                command.Parameters.AddWithValue("@recoverable_exp", RecoverableExp);
                command.Parameters.AddWithValue("@hp", Hp);
                command.Parameters.AddWithValue("@mp", Mp);
                command.Parameters.AddWithValue("@consumed_lp", ConsumedLaborPower);
                command.Parameters.AddWithValue("@ability1", (byte)Ability1);
                command.Parameters.AddWithValue("@ability2", (byte)Ability2);
                command.Parameters.AddWithValue("@ability3", (byte)Ability3);
                command.Parameters.AddWithValue("@world_id", MainWorldPosition?.WorldId ?? Transform.WorldId);
                command.Parameters.AddWithValue("@zone_id", MainWorldPosition?.ZoneId ?? Transform.ZoneId);
                command.Parameters.AddWithValue("@x", MainWorldPosition?.World.Position.X ?? Transform.World.Position.X);
                command.Parameters.AddWithValue("@y", MainWorldPosition?.World.Position.Y ?? Transform.World.Position.Y);
                command.Parameters.AddWithValue("@z", MainWorldPosition?.World.Position.Z ?? Transform.World.Position.Z);
                command.Parameters.AddWithValue("@roll", MainWorldPosition?.World.Rotation.X ?? Transform.World.Rotation.X);
                command.Parameters.AddWithValue("@pitch", MainWorldPosition?.World.Rotation.Y ?? Transform.World.Rotation.Y);
                command.Parameters.AddWithValue("@yaw", MainWorldPosition?.World.Rotation.Z ?? Transform.World.Rotation.Z);
                command.Parameters.AddWithValue("@faction_id", Faction.Id);
                command.Parameters.AddWithValue("@faction_name", FactionName);
                command.Parameters.AddWithValue("@expedition_id", Expedition?.Id ?? 0);
                command.Parameters.AddWithValue("@family", Family);
                command.Parameters.AddWithValue("@dead_count", DeadCount);
                command.Parameters.AddWithValue("@dead_time", DeadTime);
                command.Parameters.AddWithValue("@rez_wait_duration", RezWaitDuration);
                command.Parameters.AddWithValue("@rez_time", RezTime);
                command.Parameters.AddWithValue("@rez_penalty_duration", RezPenaltyDuration);
                command.Parameters.AddWithValue("@leave_time", LeaveTime);
                command.Parameters.AddWithValue("@money", Money);
                command.Parameters.AddWithValue("@money2", Money2);
                command.Parameters.AddWithValue("@honor_point", HonorPoint);
                command.Parameters.AddWithValue("@vocation_point", VocationPoint);
                command.Parameters.AddWithValue("@crime_point", CrimePoint);
                command.Parameters.AddWithValue("@crime_record", CrimeRecord);
                command.Parameters.AddWithValue("@jury_point", JuryPoint);
                command.Parameters.AddWithValue("@hostile_faction_kills", HostileFactionKills);
                command.Parameters.AddWithValue("@pvp_honor", HonorGainedInCombat);
                command.Parameters.AddWithValue("@delete_request_time", DeleteRequestTime);
                command.Parameters.AddWithValue("@transfer_request_time", TransferRequestTime);
                command.Parameters.AddWithValue("@delete_time", DeleteTime);
                command.Parameters.AddWithValue("@auto_use_aapoint", AutoUseAAPoint);
                command.Parameters.AddWithValue("@prev_point", PrevPoint);
                command.Parameters.AddWithValue("@point", Point);
                command.Parameters.AddWithValue("@gift", Gift);
                command.Parameters.AddWithValue("@num_inv_slot", NumInventorySlots);
                command.Parameters.AddWithValue("@num_bank_slot", NumBankSlots);
                command.Parameters.AddWithValue("@expanded_expert", ExpandedExpert);
                command.Parameters.AddWithValue("@slots", GetActionSlotsAsBlob());
                command.Parameters.AddWithValue("@created_at", Created);
                command.Parameters.AddWithValue("@updated_at", Updated);
                command.Parameters.AddWithValue("@return_district", ReturnDistrictId);
                command.Parameters.AddWithValue("@online_time", OnlineTime.TotalSeconds);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                foreach (var pair in _options)
                {
                    command.CommandText =
                        "REPLACE INTO `options` (`key`,`value`,`owner`) VALUES (@key,@value,@owner)";
                    command.Parameters.AddWithValue("@key", pair.Key);
                    command.Parameters.AddWithValue("@value", pair.Value);
                    command.Parameters.AddWithValue("@owner", Id);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                }
            }

            // Inventory?.Save(connection, transaction);
            Abilities?.Save(connection, transaction);
            Actability?.Save(connection, transaction);
            Appellations?.Save(connection, transaction);
            Portals?.Save(connection, transaction);
            Friends?.Save(connection, transaction);
            Blocked?.Save(connection, transaction);
            Skills?.Save(connection, transaction);
            Quests?.Save(connection, transaction);
            Mates?.Save(connection, transaction);
            Attendances?.Save(connection, transaction);
            result = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            result = false;
        }

        return result;
    }

    #endregion

    public override void AddVisibleObject(Character character)
    {
        if (this != character) // Never send to self, or the client crashes
            character.SendPacket(new SCUnitStatePacket(this));
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp, HighAbilityRsc));
        /*
        // If player is hanging on something, also send a hung packet, this should work in theory, but doesn't
        if (this.Transform.StickyParent != null)
            character.SendPacket(new SCHungPacket(this.ObjId,this.Transform.StickyParent.GameObject.ObjId));
        */
        base.AddVisibleObject(character);
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);

        if (this != character) // Never send to self, or the client crashes
            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
    }

    public PacketStream Write(PacketStream stream)
    {
        #region Character_List_Packet_DDB0
        stream.Write(Id);           // id
        stream.Write(Name);         // name
        stream.Write((byte)Race);   // CharRace
        stream.Write((byte)Gender); // CharGender
        stream.Write(Level);        // level
        stream.Write(HeirExp);      // heirExp add for 3.5.0.3 : uint in 3.5, long in 5.7
        stream.Write(Hp);           // health
        stream.Write(Mp);           // mana
        stream.Write(Transform.ZoneId); // zid
        stream.Write((uint)Faction.Id);      // type
        stream.Write(FactionName);     // factionName
        stream.Write((uint)(Expedition?.Id ?? 0)); // type
        stream.Write(Family);          // family

        #region CharacterInfo_3EB0

        Inventory_Equip(stream);

        #endregion CharacterInfo_3EB0

        stream.Write((byte)Ability1);
        stream.Write((byte)Ability2);
        stream.Write((byte)Ability3);

        stream.Write(Helpers.ConvertLongX(Transform.Local.Position.X));
        stream.Write(Helpers.ConvertLongY(Transform.Local.Position.Y));
        stream.Write(Transform.Local.Position.Z);

        #region CustomModel
        stream.Write(ModelParams);
        #endregion

        stream.Write(DeadCount);          // deadCount
        stream.Write(DeadTime);           // deadTime
        stream.Write(RezWaitDuration);    // rezWaitDuration
        stream.Write(RezTime);            // rezTime
        stream.Write(RezPenaltyDuration); // rezPenaltyDuration
        stream.Write(LeaveTime);   // lastWorldLeaveTime
        stream.Write(Money);       // moneyAmount
        stream.Write(0L);          // moneyAmount
        stream.Write(CrimePoint);  // current crime points (/50) short in 3+ , int in 1.2, short in 5.0
        stream.Write(CrimeRecord); // total infamy 
        stream.Write(CrimeScore);  // crimeScore for 1.2
        stream.Write(DeleteRequestTime);
        stream.Write(TransferRequestTime);
        stream.Write(CreatedTime);    // createdTime add in 5.0
        stream.Write(DeleteTime);     // deleteDelay
        stream.Write(Money2);         // moneyAmount
        stream.Write(0L);             // moneyAmount
        stream.Write(AutoUseAAPoint); // TODO in x2game byte not bool
        stream.Write(PrevPoint);      // prevPoint
        stream.Write(Point);          // point
        stream.Write(Gift);           // gift
        stream.Write(Updated);        // updated
        stream.Write(ForceNameChange);// forceNameChange
        stream.Write(HighAbilityRsc); // highAbilityRsc for 3.0.3.0

        stream.Write(Guid);           // guid added in 5.0, 4.5 (16 bytes)

        #region sub_398D6CC0
        stream.Write(LaborPower);         // lp
        stream.Write(LocalLaborPower);    // localLaborPower  add in 3042 localLaborPower, moved in 5.0
        stream.Write(ConsumedLaborPower); // consumed
        stream.Write(LaborPowerModified); // updated
        stream.Write(BmPoint);            // moved in 5.0
        stream.Write(RechargedLp);        // rechargedLp
        stream.Write(RechargeResetTime);  // rechargeResetTime
        #endregion

        return stream;
        #endregion
    }

    private void Inventory_Equip(PacketStream stream)
    {
        #region Inventory_Equip

        var index = 0;
        var validFlags = 0;
        // calculate validFlags
        var items = Inventory.Equipment.GetSlottedItemsList();
        foreach (var item in items)
        {
            if (item != null)
            {
                validFlags |= 1 << index;
            }

            index++;
        }

        stream.Write((uint)validFlags); // validFlags for 3.0.3.0
        foreach (var item in items)
        {
            if (item != null)
            {
                stream.Write(item);
            }
        }

        index = 0;
        validFlags = 0;

        foreach (var item in Inventory.Equipment.GetSlottedItemsList())
        {
            if (item == null) { continue; }

            var _tmp = (int)item.ItemFlags << index;
            ++index;
            validFlags |= _tmp;
        }
        stream.Write(validFlags); //  ItemFlags flags for 3.0.3.0

        #endregion Inventory_Equip
    }

    /// <summary>
    /// Adds crime, and returns the new (current) crime value
    /// </summary>
    /// <param name="amount"></param>
    /// <returns></returns>
    public short AddCrime(int amount)
    {
        var newAmount = CrimePoint + amount;
        if (newAmount > short.MaxValue)
        {
            CrimePoint = short.MaxValue; // current crime point can't go over short MaxValue
        }
        if (newAmount < 0)
        {
            CrimePoint = 0;
        }
        else
        {
            CrimePoint = (short)newAmount;
        }
        CrimeRecord += amount; // total amount
        if (CrimeRecord < 0)
            CrimeRecord = 0;
        return CrimePoint;
    }

    public override string DebugName()
    {
        return base.DebugName() + " (" + Id + ")";
    }
}
