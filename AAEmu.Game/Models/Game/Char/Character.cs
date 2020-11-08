using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
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

    public class Character : Unit
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

        public int AccessLevel { get; set;}
        public Point LocalPingPosition { get; set; } // added as a GM command helper

        private bool _inParty;
        private bool _isOnline;

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

        public int Str
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Str);
                var parameters = new Dictionary<string, double> {["level"] = Level};
                var result = formula.Evaluate(parameters);
                var res = (int)result;
                foreach (var item in Inventory.Equipment.Items)
                    if (item is EquipItem equip)
                        res += equip.Str;
                foreach (var bonus in GetBonuses(UnitAttribute.Str))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        public int Dex
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Dex);
                var parameters = new Dictionary<string, double> {["level"] = Level};
                var res = (int)formula.Evaluate(parameters);
                foreach (var item in Inventory.Equipment.Items)
                    if (item is EquipItem equip)
                        res += equip.Dex;
                foreach (var bonus in GetBonuses(UnitAttribute.Dex))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        public int Sta
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Sta);
                var parameters = new Dictionary<string, double> {["level"] = Level};
                var res = (int)formula.Evaluate(parameters);
                foreach (var item in Inventory.Equipment.Items)
                    if (item is EquipItem equip)
                        res += equip.Sta;
                foreach (var bonus in GetBonuses(UnitAttribute.Sta))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        public int Int
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Int);
                var parameters = new Dictionary<string, double> {["level"] = Level};
                var res = (int)formula.Evaluate(parameters);
                foreach (var item in Inventory.Equipment.Items)
                    if (item is EquipItem equip)
                        res += equip.Int;
                foreach (var bonus in GetBonuses(UnitAttribute.Int))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        public int Spi
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Spi);
                var parameters = new Dictionary<string, double> {["level"] = Level};
                var res = (int)formula.Evaluate(parameters);
                foreach (var item in Inventory.Equipment.Items)
                    if (item is EquipItem equip)
                        res += equip.Spi;
                foreach (var bonus in GetBonuses(UnitAttribute.Spi))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        public int Fai
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Fai);
                var parameters = new Dictionary<string, double> {["level"] = Level};
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Fai))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

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
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxHealth))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

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
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.HealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

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
                var res = (int)formula.Evaluate(parameters);
                res /= 5; // TODO ...
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

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
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxMana))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

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
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.ManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

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
                var res = (int)formula.Evaluate(parameters);
                res /= 5; // TODO ...
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
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
                parameters["ab_level"] = 0;
                var res = formula.Evaluate(parameters);
                return (float)res;
            }
        }

        public override int Dps
        {
            get
            {
                var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
                var res = weapon?.Dps ?? 0;
                res += Str / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.MainhandDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

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
                foreach (var bonus in GetBonuses(UnitAttribute.MeleeDpsInc))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)res;
            }
        }

        public override int OffhandDps
        {
            get
            {
                var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
                var res = weapon?.Dps ?? 0;
                res += Str / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.OffhandDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

        public override int RangedDps
        {
            get
            {
                var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Ranged);
                var res = weapon?.Dps ?? 0;
                res += Dex / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.RangedDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

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
                foreach (var bonus in GetBonuses(UnitAttribute.RangedDpsInc))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)res;
            }
        }

        public override int MDps
        {
            get
            {
                var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
                var res = weapon?.MDps ?? 0;
                res += Int / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.SpellDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

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
                foreach (var bonus in GetBonuses(UnitAttribute.SpellDpsInc))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)res;
            }
        }
        
        public override int HDps
        {
            get
            {
                var weapon = (Weapon) Inventory.Equipment.GetItemBySlot((int) EquipmentItemSlot.Mainhand);
                var res = weapon?.HDps ?? 0;
                res += Spi / 5f;
                foreach(var bonus in GetBonuses(UnitAttribute.HealDps))
                {
                    if(bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return (int) (res * 1000);
            }
        }

        public override int HDpsInc
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                var res = formula.Evaluate(parameters);
                foreach(var bonus in GetBonuses(UnitAttribute.SpellDpsInc))
                {
                    if(bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return (int) res;
            }
        }

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

                foreach (var bonus in GetBonuses(UnitAttribute.Armor))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

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

                foreach (var bonus in GetBonuses(UnitAttribute.MagicResist))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        #endregion

        public Character(UnitCustomModelParams modelParams)
        {
            _options = new Dictionary<ushort, string>();

            ModelParams = modelParams;
            Subscribers = new List<IDisposable>();
        }
        
        public WeaponWieldKind GetWeaponWieldKind()
        {
            var item = Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
            if (item.Template is WeaponTemplate weapon)
            {
                EquipmentItemSlotType slotId = (EquipmentItemSlotType)weapon.HoldableTemplate.SlotTypeId;
                if (slotId == EquipmentItemSlotType.TwoHanded)
                    return WeaponWieldKind.TwoHanded;
                else if (slotId == EquipmentItemSlotType.OneHanded)
                {
                    var item2 = Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
                    if (item2 != null && item2.Template is WeaponTemplate weapon2)
                    {
                        EquipmentItemSlotType slotId2 = (EquipmentItemSlotType)weapon2.HoldableTemplate.SlotTypeId;
                        if (slotId2 == EquipmentItemSlotType.OneHanded)
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

        public void AddExp(int exp, bool shouldAddAbilityExp)
        {
            var expMultiplier = 1d;
            if (exp == 0)
                return;
            if (float.TryParse(ConfigurationManager.Instance.GetConfiguration("ExperienceMultiplierInPercent"), out var xpm))
                expMultiplier = xpm / 100f;
            var totalExp = Math.Round(expMultiplier * exp);
            exp = (int)totalExp;
            Expirience += exp;
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

        public bool ChangeMoney(SlotType moneylocation, int amount) => ChangeMoney(SlotType.None, moneylocation, amount);

        public bool ChangeMoney(SlotType typeFrom, SlotType typeTo, int amount)
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
            SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.DepositMoney, itemTasks, new List<ulong>()));
            return true;
        }

        public bool AddMoney(SlotType moneyLocation,int amount)
        {
            if (amount < 0)
                return false;
            return ChangeMoney(SlotType.None, moneyLocation, amount);
        }

        public bool SubtractMoney(SlotType moneyLocation, int amount)
        {
            if (amount < 0)
                return false;
            return ChangeMoney(SlotType.None, moneyLocation, -amount);
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
                        "(`id`,`account_id`,`name`,`access_level`,`race`,`gender`,`unit_model_params`,`level`,`expirience`,`recoverable_exp`,`hp`,`mp`,`labor_power`,`labor_power_modified`,`consumed_lp`,`ability1`,`ability2`,`ability3`,`world_id`,`zone_id`,`x`,`y`,`z`,`rotation_x`,`rotation_y`,`rotation_z`,`faction_id`,`faction_name`,`expedition_id`,`family`,`dead_count`,`dead_time`,`rez_wait_duration`,`rez_time`,`rez_penalty_duration`,`leave_time`,`money`,`money2`,`honor_point`,`vocation_point`,`crime_point`,`crime_record`,`delete_request_time`,`transfer_request_time`,`delete_time`,`bm_point`,`auto_use_aapoint`,`prev_point`,`point`,`gift`,`num_inv_slot`,`num_bank_slot`,`expanded_expert`,`slots`,`updated_at`) " +
                        "VALUES(@id,@account_id,@name,@access_level,@race,@gender,@unit_model_params,@level,@expirience,@recoverable_exp,@hp,@mp,@labor_power,@labor_power_modified,@consumed_lp,@ability1,@ability2,@ability3,@world_id,@zone_id,@x,@y,@z,@rotation_x,@rotation_y,@rotation_z,@faction_id,@faction_name,@expedition_id,@family,@dead_count,@dead_time,@rez_wait_duration,@rez_time,@rez_penalty_duration,@leave_time,@money,@money2,@honor_point,@vocation_point,@crime_point,@crime_record,@delete_request_time,@transfer_request_time,@delete_time,@bm_point,@auto_use_aapoint,@prev_point,@point,@gift,@num_inv_slot,@num_bank_slot,@expanded_expert,@slots,@updated_at)";

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
