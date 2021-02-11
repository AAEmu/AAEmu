using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char
{
    public enum Race : byte
    {
        None = 0,
        Nuian = 1,
        Fairy = 2,
        Dwarf = 3,
        Elf = 4,
        Hariharan = 5,
        Ferre = 6,
        Returned = 7,
        Warborn = 8
    }

    public enum Gender : byte
    {
        Male = 1,
        Female = 2
    }

    public partial class Character : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Character;
        public static Dictionary<uint, uint> _usedCharacterObjIds = new Dictionary<uint, uint>();

        private Dictionary<ushort, string> _options;

        public List<IDisposable> Subscribers { get; set; }

        public uint Id { get; set; }
        public uint AccountId { get; set; }
        public Race Race { get; set; }
        public Gender Gender { get; set; }
        public short LaborPower { get; set; }
        public DateTime LaborPowerModified { get; set; }
        public int ConsumedLaborPower { get; set; }
        public AbilityType Ability1 { get; set; }
        public AbilityType Ability2 { get; set; }
        public AbilityType Ability3 { get; set; }
        public DateTime LastCombatActivity { get; set; }
        public DateTime LastCast { get; set; }
        public bool IsInCombat { get; set; }
        public bool IsInPostCast { get; set; }
        public bool IgnoreSkillCooldowns { get; set; }
        public string FactionName { get; set; }
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
        public DateTime DeleteRequestTime { get; set; }
        public DateTime TransferRequestTime { get; set; }
        public DateTime DeleteTime { get; set; }
        public long BmPoint { get; set; }
        public bool AutoUseAAPoint { get; set; }
        public int PrevPoint { get; set; }
        public int Point { get; set; }
        public int Gift { get; set; }
        public int Expirience { get; set; }
        public int RecoverableExp { get; set; }
        public DateTime Updated { get; set; }
        public int PvPHonor { get; set; }
        public int PvPKills { get; set; }

        public uint ReturnDictrictId { get; set; }
        public uint ResurrectionDictrictId { get; set; }

        public override UnitCustomModelParams ModelParams { get; set; }
        public override float Scale => 1f;
        public override byte RaceGender => (byte)(16 * (byte)Gender + (byte)Race);

        public CharacterVisualOptions VisualOptions { get; set; }

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

        public byte ExpandedExpert { get; set; }
        public CharacterActability Actability { get; set; }

        public CharacterSkills Skills { get; set; }
        public CharacterCraft Craft { get; set; }

        public int AccessLevel { get; set; }
        public Point LocalPingPosition { get; set; } // added as a GM command helper
        private ConcurrentDictionary<uint, DateTime> _hostilePlayers { get; set; }

        private bool _inParty;
        private bool _isOnline;

        private bool _isUnderWater;
        public bool IsUnderWater
        {
            get { return _isUnderWater; }
            set
            {
                _isUnderWater = value;
                if (!_isUnderWater)
                    Breath = LungCapacity;
                SendPacket(new SCUnderWaterPacket(_isUnderWater));
            }
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
                if(!value) TeamManager.Instance.SetOffline(this);
                _isOnline = value;
            }
        }

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
                var parameters = new Dictionary<string, double> { ["level"] = Level };
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
                var parameters = new Dictionary<string, double> { ["level"] = Level };
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
                var parameters = new Dictionary<string, double> { ["level"] = Level };
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
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = formula.Evaluate(parameters);
                foreach (var item in Inventory.Equipment.Items)
                    if (item is EquipItem equip)
                        res += equip.Int;
                res = CalculateWithBonuses(res, UnitAttribute.Int);

                return (int)res;
            }
        }

        public int Spi
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Spi);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
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
                var parameters = new Dictionary<string, double> { ["level"] = Level };
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MaxHealth);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealthRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character,
                    UnitFormulaKind.PersistentHealthRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MaxMana);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.ManaRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character,
                    UnitFormulaKind.PersistentManaRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.LevelDps);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.RangedDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.SpellDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeAntiMiss);
                var parameters = new Dictionary<string, double>();
                parameters["str"] = Str; //Str not needed, but maybe we use later
                parameters["spi"] = Spi;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeCritical);
                var parameters = new Dictionary<string, double>();
                parameters["str"] = Str; //Str not needed, but maybe we use later
                parameters["dex"] = Dex;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.RangedAntiMiss);
                var parameters = new Dictionary<string, double>();
                parameters["dex"] = Dex; //Str not needed, but maybe we use later
                parameters["spi"] = Spi;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.RangedCritical);
                var parameters = new Dictionary<string, double>();
                parameters["dex"] = Dex; //Str not needed, but maybe we use later
                parameters["int"] = Int;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.SpellAntiMiss);
                var parameters = new Dictionary<string, double>();
                parameters["int"] = Int;
                parameters["spi"] = Spi;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.SpellCritical);
                var parameters = new Dictionary<string, double>();
                parameters["int"] = Int; //Str not needed, but maybe we use later
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealCritical);
                var parameters = new Dictionary<string, double>();
                parameters["spi"] = Spi; //Str not needed, but maybe we use later
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
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MagicResist);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Facet);
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Dodge);
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeParry);
                var parameters = new Dictionary<string, double>();
                parameters["str"] = Str;
                parameters["sta"] = Sta;
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
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Block);
                var parameters = new Dictionary<string, double>();
                parameters["str"] = Str;
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
            get=> (float)CalculateWithBonuses(1d, UnitAttribute.Block);
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
        }

        public WeaponWieldKind GetWeaponWieldKind()
        {
            var item = Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
            if (item != null && item.Template is WeaponTemplate weapon)
            {
                var slotId = (EquipmentItemSlotType)weapon.HoldableTemplate.SlotTypeId;
                if (slotId == EquipmentItemSlotType.TwoHanded)
                    return WeaponWieldKind.TwoHanded;
                else if (slotId == EquipmentItemSlotType.OneHanded || slotId == EquipmentItemSlotType.Mainhand)
                {
                    var item2 = Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
                    if (item2 != null && item2.Template is WeaponTemplate weapon2)
                    {
                        var slotId2 = (EquipmentItemSlotType)weapon2.HoldableTemplate.SlotTypeId;
                        if (slotId2 == EquipmentItemSlotType.OneHanded || slotId2 == EquipmentItemSlotType.Offhand)
                            return WeaponWieldKind.DuelWielded;
                        else
                            return WeaponWieldKind.OneHanded;
                    }
                    else
                        return WeaponWieldKind.OneHanded;
                }
            }

            return WeaponWieldKind.None;
        }

        public void SetHostileActivity(Character attacker)
        {
            if (_hostilePlayers.ContainsKey(attacker.ObjId))
                _hostilePlayers[attacker.ObjId] = DateTime.Now;
            else
                _hostilePlayers.TryAdd(attacker.ObjId, DateTime.Now);
        }

        public override void DoDie(Unit killer)
        {
            base.DoDie(killer);
            _hostilePlayers.Clear(); // Is this correct?
        }

        public List<uint> getHostilePlayers()
        {
            return new List<uint>(_hostilePlayers.Keys);
        }

        public List<Character> getActivelyHostilePlayers()
        {
            var players = new List<Character>();
            foreach (var objId in _hostilePlayers.Keys)
            {
                var character = WorldManager.Instance.GetCharacterByObjId(objId);
                if (character != null && IsActivelyHostile(character))
                    players.Add(character);
            }

            return players;
        }

        public bool IsActivelyHostile(Character target)
        {
            if(_hostilePlayers.TryGetValue(target.ObjId, out var value))
            {
                //Maybe get the time to stay hostile from db?
                return value.AddSeconds(30) > DateTime.Now;
            }
            return false;
        }

        public void AddHonorFromKill(int honor, bool isKill = false)
        {
            HonorPoint += honor;
            PvPHonor += honor;

            SendPacket(new SCGamePointChangedPacket(0, honor));
            BroadcastPacket(new SCUnitPvPPointsChangedPacket(ObjId, 0, PvPHonor), true);

            if (isKill)
            {
                PvPKills++;
                BroadcastPacket(new SCUnitPvPPointsChangedPacket(ObjId, 1, PvPKills), true);
            }
        }

        public void AddExp(int exp, bool shouldAddAbilityExp)
        {
            var expMultiplier = 1d;
            if (exp == 0)
                return;
            if (float.TryParse(ConfigurationManager.Instance.GetConfiguration("ExperienceMultiplierInPercent"), out var xpm))
                expMultiplier = xpm / 100f;
            var totalExp = Math.Round(expMultiplier * exp);
            exp = (int)totalExp;
            Expirience = Math.Min(Expirience + exp, ExpirienceManager.Instance.GetExpForLevel(55));
            if (shouldAddAbilityExp)
                Abilities.AddActiveExp(exp); // TODO ... or all?
            SendPacket(new SCExpChangedPacket(ObjId, exp, shouldAddAbilityExp));
            CheckLevelUp();
        }

        public void CheckLevelUp()
        {
            var needExp = ExpirienceManager.Instance.GetExpForLevel((byte)(Level + 1));
            var change = false;
            while (Expirience >= needExp)
            {
                change = true;
                Level++;
                needExp = ExpirienceManager.Instance.GetExpForLevel((byte)(Level + 1));
            }

            if (change)
            {
                BroadcastPacket(new SCLevelChangedPacket(ObjId, Level), true);
                StartRegen();

                Quests.OnLevelUp();
            }
        }

        public void CheckExp()
        {
            var needExp = ExpirienceManager.Instance.GetExpForLevel(Level);
            if (Expirience < needExp)
                Expirience = needExp;
            needExp = ExpirienceManager.Instance.GetExpForLevel((byte)(Level + 1));
            while (Expirience >= needExp)
            {
                Level++;
                needExp = ExpirienceManager.Instance.GetExpForLevel((byte)(Level + 1));
            }
        }

        public bool ChangeMoney(SlotType moneylocation, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney) => ChangeMoney(SlotType.None, moneylocation, amount, itemTaskType);

        public bool ChangeMoney(SlotType typeFrom, SlotType typeTo, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
        {
            var itemTasks = new List<ItemTask>();
            switch(typeFrom)
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

        public bool AddMoney(SlotType moneyLocation,int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
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
                Actability.AddPoint((uint)actabilityId, actabilityChange);
            }

            LaborPower += change;
            SendPacket(new SCCharacterLaborPowerChangedPacket(change, actabilityId, actabilityChange, actabilityStep));
        }

        public override int GetAbLevel(AbilityType type)
        {
            if (type == AbilityType.General) return Level;
            return ExpirienceManager.Instance.GetLevelFromExp(Abilities.Abilities[type].Exp);
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
            foreach(var skillId in skillIds)
            {
                packets.AddPacket(new SCSkillCooldownResetPacket(this, skillId, 0, triggerGcd));
            }
            SendPacket(packets);
        }

        public void SetPirate(bool pirate)
        {
            // TODO : If castle owner -> Nope
            var defaultFactionId = CharacterManager.Instance.GetTemplate((byte)Race, (byte)Gender).FactionId;

            var newFaction = pirate ? (uint)Factions.FACTION_PIRATE : defaultFactionId;
            BroadcastPacket(new SCUnitFactionChangedPacket(ObjId, Name, Faction.Id, newFaction, false), true);
            Faction = FactionManager.Instance.GetFaction(newFaction);
            HousingManager.Instance.UpdateOwnedHousingFaction(Id, newFaction);
            // TODO : Teleport to Growlgate
            // TODO : Leave guild
        }

        public void SetFaction(uint factionId)
        {
            BroadcastPacket(new SCUnitFactionChangedPacket(ObjId, Name, Faction.Id, factionId, false), true);
            Faction = FactionManager.Instance.GetFaction(factionId);
        }
        
        public override void SetPosition(float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ)
        {
            var moved = !Position.X.Equals(x) || !Position.Y.Equals(y) || !Position.Z.Equals(z);
            var lastZoneKey = Position.ZoneId;
            base.SetPosition(x, y, z, rotationX, rotationY, rotationZ);

            if (!IsUnderWater && Position.Z < 98) //TODO: Need way to determine when player is under any body of water. 
                IsUnderWater = true;
            else if (IsUnderWater && Position.Z > 98)
                IsUnderWater = false;
            
            if (!moved)
                return;

            Buffs.TriggerRemoveOn(BuffRemoveOn.Move);

            if (Position.ZoneId == lastZoneKey)
                return;
            
            OnZoneChange(lastZoneKey,Position.ZoneId);
        }

        public void OnZoneChange(uint lastZoneKey, uint newZoneKey)
        {
            // We switched zonekeys, we need to do some checks
            var lastZone = ZoneManager.Instance.GetZoneByKey(lastZoneKey);
            var newZone = ZoneManager.Instance.GetZoneByKey(newZoneKey);
            var lastZoneGroupId = (short)(lastZone?.GroupId ?? 0);
            var newZoneGroupId = (short)(newZone?.GroupId ?? 0);
            if (lastZoneGroupId == newZoneGroupId)
                return;

            // Handle Zone Buffs
            if (lastZone != null)
            {
                // Remove the old zone buff if needed
                var lastZoneGroup = ZoneManager.Instance.GetZoneGroupById(lastZone.GroupId);
                if ((lastZoneGroup != null) && (lastZoneGroup.BuffId != 0))
                {
                    // Remove the applied buff from last zonegroup
                    Buffs.RemoveBuff(lastZoneGroup.BuffId);
                }
            }
            if (newZone != null)
            {
                // Apply the new zone buff if needed
                var newZoneGroup = ZoneManager.Instance.GetZoneGroupById(newZone.GroupId);
                if ((newZoneGroup != null) && (newZoneGroup.BuffId != 0))
                {
                    // Add buff from new zonegroup
                    var buffTemplate = SkillManager.Instance.GetBuffTemplate(newZoneGroup.BuffId);
                    if (buffTemplate != null)
                    {
                        var casterObj = new SkillCasterUnit(ObjId);
                        var newZoneBuff = new Buff(this, this, casterObj, buffTemplate, null, System.DateTime.Now);
                        Buffs.AddBuff(newZoneBuff);
                    }
                }
            }

            // Ok, we actually changed zone groups, we'll leave to do some chat channel stuff
            if (lastZoneGroupId != 0)
                ChatManager.Instance.GetZoneChat(lastZoneKey).LeaveChannel(this);

            if (newZoneGroupId != 0)
                ChatManager.Instance.GetZoneChat(Position.ZoneId).JoinChannel(this);

            if (newZone != null && (!newZone.Closed))
                return;

            // Entered a forbidden zone
            /*
                            if (!thisChar.isGM)
                            {
                                // TODO: for non-GMs, add a timed task to kick them out (recall to last Nui)
                                // TODO: Remove backpack immediately
                            }
                            */
            // Send extra info to player if we are still in a real but unreleased zone (not null), this is not retail behaviour
            if (newZone != null)
                SendMessage(ChatType.System,
                    "|cFFFF0000You have entered a closed zone ({0} - {1})!\nPlease leave immediately!|r",
                    newZone.ZoneKey, newZone.Name);
            // Send the error message
            SendErrorMessage(ErrorMessageType.ClosedZone);
        }

        public void DoFallDamage(ushort fallVel)
        {
            var fallDmg = Math.Min(Hp,(int)(Hp * ((fallVel-5000) / 20000f)));
            ReduceCurrentHp(this, fallDmg);

            SendPacket(new SCEnvDamagePacket(EnvSource.Falling, ObjId, (uint) fallDmg));
            
            //todo stun & maybe adjust formula?
            _log.Warn("FallDamage: Vel{0} DmgPerc: {1}", fallVel, (int)((fallVel - 5000) / 200f));
        }
        
        public void SetAction(byte slot, ActionSlotType type, uint actionId)
        {
            Slots[slot].Type = type;
            Slots[slot].ActionId = actionId;
        }

        public void SetOption(ushort key, string value)
        {
            if (_options.ContainsKey(key))
                _options[key] = value;
            else
                _options.Add(key, value);
        }

        public string GetOption(ushort key)
        {
            if (_options.ContainsKey(key))
                return _options[key];
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

        public void SendMessage(string message, params object[] parameters)
        {
            SendMessage(ChatType.System, message, parameters);
        }

        public void SendMessage(ChatType type, string message, params object[] parameters)
        {
            SendPacket(new SCChatMessagePacket(type, string.Format(message, parameters)));
        }

        public void SendErrorMessage(ErrorMessageType errorMsgType, uint type = 0, bool isNotify = true)
        {
            SendPacket(new SCErrorMsgPacket(errorMsgType, type, isNotify));
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
                character.SendPacket(packet);
            if (self)
                SendPacket(packet);
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

        #region Database

        public static Character Load(MySqlConnection connection, uint characterId, uint accountId)
        {
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
                        character.Position = new Point();
                        character.AccountId = accountId;
                        character.Id = reader.GetUInt32("id");
                        character.Name = reader.GetString("name");
                        character.AccessLevel = reader.GetInt32("access_level");
                        character.Race = (Race)reader.GetByte("race");
                        character.Gender = (Gender)reader.GetByte("gender");
                        character.Level = reader.GetByte("level");
                        character.Expirience = reader.GetInt32("expirience");
                        character.RecoverableExp = reader.GetInt32("recoverable_exp");
                        character.Hp = reader.GetInt32("hp");
                        character.Mp = reader.GetInt32("mp");
                        character.LaborPower = reader.GetInt16("labor_power");
                        character.LaborPowerModified = reader.GetDateTime("labor_power_modified");
                        character.ConsumedLaborPower = reader.GetInt32("consumed_lp");
                        character.Ability1 = (AbilityType)reader.GetByte("ability1");
                        character.Ability2 = (AbilityType)reader.GetByte("ability2");
                        character.Ability3 = (AbilityType)reader.GetByte("ability3");
                        character.Position.WorldId = reader.GetUInt32("world_id");
                        character.Position.ZoneId = reader.GetUInt32("zone_id");
                        character.Position.X = reader.GetFloat("x");
                        character.Position.Y = reader.GetFloat("y");
                        character.Position.Z = reader.GetFloat("z");
                        character.Position.RotationX = reader.GetSByte("rotation_x");
                        character.Position.RotationY = reader.GetSByte("rotation_y");
                        character.Position.RotationZ = reader.GetSByte("rotation_z");
                        character.Faction = FactionManager.Instance.GetFaction(reader.GetUInt32("faction_id"));
                        character.FactionName = reader.GetString("faction_name");
                        character.Expedition = ExpeditionManager.Instance.GetExpedition(reader.GetUInt32("expedition_id"));
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
                        character.TransferRequestTime = reader.GetDateTime("transfer_request_time");
                        character.DeleteRequestTime = reader.GetDateTime("delete_request_time");
                        character.DeleteTime = reader.GetDateTime("delete_time");
                        character.BmPoint = reader.GetInt32("bm_point");
                        character.AutoUseAAPoint = reader.GetBoolean("auto_use_aapoint");
                        character.PrevPoint = reader.GetInt32("prev_point");
                        character.Point = reader.GetInt32("point");
                        character.Gift = reader.GetInt32("gift");
                        character.NumInventorySlots = reader.GetByte("num_inv_slot");
                        character.NumBankSlots = reader.GetInt16("num_bank_slot");
                        character.ExpandedExpert = reader.GetByte("expanded_expert");
                        character.Updated = reader.GetDateTime("updated_at");
                        character.PvPHonor = reader.GetInt32("pvp_honor");
                        character.PvPKills = reader.GetInt32("pvp_kills");
                        character.ReturnDictrictId = reader.GetUInt32("return_district_id");

                        character.Inventory = new Inventory(character);

                        if (character.Hp > character.MaxHp)
                            character.Hp = character.MaxHp;
                        if (character.Mp > character.MaxMp)
                            character.Mp = character.MaxMp;
                        character.CheckExp();
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
                        character.Position = new Point();
                        character.Id = reader.GetUInt32("id");
                        character.AccountId = reader.GetUInt32("account_id");
                        character.Name = reader.GetString("name");
                        character.AccessLevel = reader.GetInt32("access_level");
                        character.Race = (Race)reader.GetByte("race");
                        character.Gender = (Gender)reader.GetByte("gender");
                        character.Level = reader.GetByte("level");
                        character.Expirience = reader.GetInt32("expirience");
                        character.RecoverableExp = reader.GetInt32("recoverable_exp");
                        character.Hp = reader.GetInt32("hp");
                        character.Mp = reader.GetInt32("mp");
                        character.LaborPower = reader.GetInt16("labor_power");
                        character.LaborPowerModified = reader.GetDateTime("labor_power_modified");
                        character.ConsumedLaborPower = reader.GetInt32("consumed_lp");
                        character.Ability1 = (AbilityType)reader.GetByte("ability1");
                        character.Ability2 = (AbilityType)reader.GetByte("ability2");
                        character.Ability3 = (AbilityType)reader.GetByte("ability3");
                        character.Position.WorldId = reader.GetUInt32("world_id");
                        character.Position.ZoneId = reader.GetUInt32("zone_id");
                        character.Position.X = reader.GetFloat("x");
                        character.Position.Y = reader.GetFloat("y");
                        character.Position.Z = reader.GetFloat("z");
                        character.Position.RotationX = reader.GetSByte("rotation_x");
                        character.Position.RotationY = reader.GetSByte("rotation_y");
                        character.Position.RotationZ = reader.GetSByte("rotation_z");
                        character.Faction = FactionManager.Instance.GetFaction(reader.GetUInt32("faction_id"));
                        character.FactionName = reader.GetString("faction_name");
                        character.Expedition = ExpeditionManager.Instance.GetExpedition(reader.GetUInt32("expedition_id"));
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
                        character.TransferRequestTime = reader.GetDateTime("transfer_request_time");
                        character.DeleteRequestTime = reader.GetDateTime("delete_request_time");
                        character.DeleteTime = reader.GetDateTime("delete_time");
                        character.BmPoint = reader.GetInt32("bm_point");
                        character.AutoUseAAPoint = reader.GetBoolean("auto_use_aapoint");
                        character.PrevPoint = reader.GetInt32("prev_point");
                        character.Point = reader.GetInt32("point");
                        character.Gift = reader.GetInt32("gift");
                        character.NumInventorySlots = reader.GetByte("num_inv_slot");
                        character.NumBankSlots = reader.GetInt16("num_bank_slot");
                        character.ExpandedExpert = reader.GetByte("expanded_expert");
                        character.Updated = reader.GetDateTime("updated_at");
                        character.PvPHonor = reader.GetInt32("pvp_honor");
                        character.PvPKills = reader.GetInt32("pvp_kills");
                        character.ReturnDictrictId = reader.GetUInt32("return_district_id");

                        character.Inventory = new Inventory(character);

                        if (character.Hp > character.MaxHp)
                            character.Hp = character.MaxHp;
                        if (character.Mp > character.MaxMp)
                            character.Mp = character.MaxMp;
                        character.CheckExp();
                    }
                }
            }

            if (character == null)
                return null;

            return character;
        }

        public void Load()
        {
            var template = CharacterManager.Instance.GetTemplate((byte)Race, (byte)Gender);
            ModelId = template.ModelId;
            BuyBackItems = new ItemContainer(this, SlotType.None,false);
            Slots = new ActionSlot[85];
            for (var i = 0; i < Slots.Length; i++)
                Slots[i] = new ActionSlot();

            Craft = new CharacterCraft(this);
            Procs = new UnitProcs(this);
            LocalPingPosition = new Point();

            using (var connection = MySQL.CreateConnection())
            {
                Inventory.Load(connection);
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
                Mates = new CharacterMates(this);
                Mates.Load(connection);

                

                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.CommandText =
                        "SELECT slots FROM `characters` WHERE `id` = @id AND `account_id` = @account_id";
                    command.Parameters.AddWithValue("@id", Id);
                    command.Parameters.AddWithValue("@account_id", AccountId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var slots = (PacketStream)((byte[])reader.GetValue("slots"));
                            foreach (var slot in Slots)
                            {
                                slot.Type = (ActionSlotType)slots.ReadByte();
                                if (slot.Type != ActionSlotType.None)
                                    slot.ActionId = slots.ReadUInt32();
                            }
                        }
                    }
                }
            }

            Mails = new CharacterMails(this);
            MailManager.Instance.GetCurrentMailList(this); //Doesn't need a connection, but does need to load after the inventory
            // Update sync housing factions on login
            HousingManager.Instance.UpdateOwnedHousingFaction(this.Id, this.Faction.Id);
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
                    catch
                    {
                        saved = false;
                        _log.Error(string.Format("Character save failed for {0} - {1}",Id, Name));
                        try
                        {
                            transaction.Rollback();
                        }
                        catch
                        {
                            // Really failed here
                            _log.Fatal(string.Format("Character save rollback failed for {0} - {1}", Id, Name));
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

                var slots = new PacketStream();
                foreach (var slot in Slots)
                {
                    slots.Write((byte)slot.Type);
                    if (slot.Type != ActionSlotType.None)
                        slots.Write(slot.ActionId);
                }

                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    // ----
                    command.CommandText =
                        "REPLACE INTO `characters` " +
                        "(`id`,`account_id`,`name`,`access_level`,`race`,`gender`,`unit_model_params`,`level`,`expirience`,`recoverable_exp`,`hp`,`mp`,`labor_power`,`labor_power_modified`,`consumed_lp`,`ability1`,`ability2`,`ability3`,`world_id`,`zone_id`,`x`,`y`,`z`,`rotation_x`,`rotation_y`,`rotation_z`,`faction_id`,`faction_name`,`expedition_id`,`family`,`dead_count`,`dead_time`,`rez_wait_duration`,`rez_time`,`rez_penalty_duration`,`leave_time`,`money`,`money2`,`honor_point`,`vocation_point`,`crime_point`,`crime_record`,`delete_request_time`,`transfer_request_time`,`delete_time`,`bm_point`,`auto_use_aapoint`,`prev_point`,`point`,`gift`,`num_inv_slot`,`num_bank_slot`,`expanded_expert`,`slots`,`updated_at`,`pvp_honor`,`pvp_kills`,`return_district_id`) " +
                        "VALUES(@id,@account_id,@name,@access_level,@race,@gender,@unit_model_params,@level,@expirience,@recoverable_exp,@hp,@mp,@labor_power,@labor_power_modified,@consumed_lp,@ability1,@ability2,@ability3,@world_id,@zone_id,@x,@y,@z,@rotation_x,@rotation_y,@rotation_z,@faction_id,@faction_name,@expedition_id,@family,@dead_count,@dead_time,@rez_wait_duration,@rez_time,@rez_penalty_duration,@leave_time,@money,@money2,@honor_point,@vocation_point,@crime_point,@crime_record,@delete_request_time,@transfer_request_time,@delete_time,@bm_point,@auto_use_aapoint,@prev_point,@point,@gift,@num_inv_slot,@num_bank_slot,@expanded_expert,@slots,@updated_at,@pvp_honor,@pvp_kills,@return_district_id)";

                    command.Parameters.AddWithValue("@id", Id);
                    command.Parameters.AddWithValue("@account_id", AccountId);
                    command.Parameters.AddWithValue("@name", Name);
                    command.Parameters.AddWithValue("@access_level", AccessLevel);
                    command.Parameters.AddWithValue("@race", (byte)Race);
                    command.Parameters.AddWithValue("@gender", (byte)Gender);
                    command.Parameters.AddWithValue("@unit_model_params", unitModelParams);
                    command.Parameters.AddWithValue("@level", Level);
                    command.Parameters.AddWithValue("@expirience", Expirience);
                    command.Parameters.AddWithValue("@recoverable_exp", RecoverableExp);
                    command.Parameters.AddWithValue("@hp", Hp);
                    command.Parameters.AddWithValue("@mp", Mp);
                    command.Parameters.AddWithValue("@labor_power", LaborPower);
                    command.Parameters.AddWithValue("@labor_power_modified", LaborPowerModified);
                    command.Parameters.AddWithValue("@consumed_lp", ConsumedLaborPower);
                    command.Parameters.AddWithValue("@ability1", (byte)Ability1);
                    command.Parameters.AddWithValue("@ability2", (byte)Ability2);
                    command.Parameters.AddWithValue("@ability3", (byte)Ability3);
                    command.Parameters.AddWithValue("@world_id", WorldPosition?.WorldId ?? Position.WorldId);
                    command.Parameters.AddWithValue("@zone_id", WorldPosition?.ZoneId ?? Position.ZoneId);
                    command.Parameters.AddWithValue("@x", WorldPosition?.X ?? Position.X);
                    command.Parameters.AddWithValue("@y", WorldPosition?.Y ?? Position.Y);
                    command.Parameters.AddWithValue("@z", WorldPosition?.Z ?? Position.Z);
                    command.Parameters.AddWithValue("@rotation_x", WorldPosition?.RotationX ?? Position.RotationX);
                    command.Parameters.AddWithValue("@rotation_y", WorldPosition?.RotationY ?? Position.RotationY);
                    command.Parameters.AddWithValue("@rotation_z", WorldPosition?.RotationZ ?? Position.RotationZ);
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
                    command.Parameters.AddWithValue("@delete_request_time", DeleteRequestTime);
                    command.Parameters.AddWithValue("@transfer_request_time", TransferRequestTime);
                    command.Parameters.AddWithValue("@delete_time", DeleteTime);
                    command.Parameters.AddWithValue("@bm_point", BmPoint);
                    command.Parameters.AddWithValue("@auto_use_aapoint", AutoUseAAPoint);
                    command.Parameters.AddWithValue("@prev_point", PrevPoint);
                    command.Parameters.AddWithValue("@point", Point);
                    command.Parameters.AddWithValue("@gift", Gift);
                    command.Parameters.AddWithValue("@num_inv_slot", NumInventorySlots);
                    command.Parameters.AddWithValue("@num_bank_slot", NumBankSlots);
                    command.Parameters.AddWithValue("@expanded_expert", ExpandedExpert);
                    command.Parameters.AddWithValue("@slots", slots.GetBytes());
                    command.Parameters.AddWithValue("@updated_at", Updated);
                    command.Parameters.AddWithValue("@pvp_honor", PvPHonor);
                    command.Parameters.AddWithValue("@pvp_kills", PvPKills);
                    command.Parameters.AddWithValue("@return_district_id", ReturnDictrictId);
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
                result = true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                result = false;
            }

            return result;
        }

        #endregion

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] {ObjId}));
        }

        public PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Name);
            stream.Write((byte)Race);
            stream.Write((byte)Gender);
            stream.Write(Level);
            stream.Write(Hp);
            stream.Write(Mp);
            stream.Write(Position.ZoneId);
            stream.Write(Faction.Id);
            stream.Write(FactionName);
            stream.Write(Expedition?.Id ?? 0);
            stream.Write(Family);

            var items = Inventory.Equipment.GetSlottedItemsList();
            foreach (var item in items)
            {
                if (item == null)
                    stream.Write(0);
                else
                    stream.Write(item);
            }

            stream.Write((byte)Ability1);
            stream.Write((byte)Ability2);
            stream.Write((byte)Ability3);

            stream.Write(Helpers.ConvertLongX(Position.X));
            stream.Write(Helpers.ConvertLongY(Position.Y));
            stream.Write(Position.Z);

            stream.Write(ModelParams);
            stream.Write(LaborPower);
            stream.Write(LaborPowerModified);
            stream.Write(DeadCount);
            stream.Write(DeadTime);
            stream.Write(RezWaitDuration);
            stream.Write(RezTime);
            stream.Write(RezPenaltyDuration);
            stream.Write(LeaveTime); // lastWorldLeaveTime
            stream.Write(Money);
            stream.Write(0L); // moneyAmount
            stream.Write(CrimePoint);
            stream.Write(CrimeRecord);
            stream.Write((short)0); // crimeScore
            stream.Write(DeleteRequestTime);
            stream.Write(TransferRequestTime);
            stream.Write(DeleteTime); // deleteDelay
            stream.Write(ConsumedLaborPower);
            stream.Write(BmPoint);
            stream.Write(Money2); //moneyAmount
            stream.Write(0L); //moneyAmount
            stream.Write(AutoUseAAPoint);
            stream.Write(PrevPoint);
            stream.Write(Point);
            stream.Write(Gift);
            stream.Write(Updated);
            stream.Write((byte)0); // forceNameChange
            return stream;
        }
    }
}
