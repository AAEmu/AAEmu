using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.DB.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;
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

    public sealed class Character : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<ushort, string> _options;

        public GameConnection Connection { get; set; }
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

        public Item[] BuyBack { get; set; }
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
                foreach (var item in Inventory.Equip)
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
                foreach (var item in Inventory.Equip)
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
                foreach (var item in Inventory.Equip)
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
                foreach (var item in Inventory.Equip)
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
                foreach (var item in Inventory.Equip)
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
                var weapon = (Weapon)Inventory.Equip[(int)EquipmentItemSlot.Mainhand];
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
                var weapon = (Weapon)Inventory.Equip[(int)EquipmentItemSlot.Offhand];
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
                var weapon = (Weapon)Inventory.Equip[(int)EquipmentItemSlot.Ranged];
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
                var weapon = (Weapon)Inventory.Equip[(int)EquipmentItemSlot.Mainhand];
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
                foreach (var item in Inventory.Equip)
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
                foreach (var item in Inventory.Equip)
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

        public void AddExp(int exp, bool shouldAddAbilityExp)
        {
            if (exp == 0)
                return;
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

        public void ChangeMoney(SlotType typeTo, int amount)
        {
            switch (typeTo)
            {
                case SlotType.Bank:
                    if ((Money - amount) >= 0)
                    {
                        Money -= amount;
                        Money2 += amount;
                        SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.DepositMoney,
                            new List<ItemTask>
                            {
                                new MoneyChange(-amount),
                                new MoneyChangeBank(amount)
                            },
                            new List<ulong>()));
                    }
                    else
                        _log.Warn("Not Money in Inventory.");

                    break;
                case SlotType.Inventory:
                    if ((Money2 - amount) >= 0)
                    {
                        Money2 -= amount;
                        Money += amount;
                        SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.WithdrawMoney,
                            new List<ItemTask>
                            {
                                new MoneyChange(amount),
                                new MoneyChangeBank(-amount)
                            },
                            new List<ulong>()));
                    }
                    else
                        _log.Warn("Not Money in Bank.");

                    break;
                default:
                    _log.Warn("Change Money!");
                    break;
            }
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

        public void SendPacket(GamePacket packet)
        {
            Connection?.SendPacket(packet);
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
            using (var ctx = new GameDBContext())
                return Load(ctx, characterId, accountId);
        }

        public static Character Load(uint characterId)
        {
            using (var ctx = new GameDBContext())
                return Load(ctx, characterId);
        }

        #region Database

        public static Character Load(GameDBContext ctx, uint characterId, uint accountId)
        {
            Character character = ctx.Characters
                .Where(c =>
                    c.Id == characterId &&
                    c.AccountId == accountId && 
                    c.Deleted == 0)
                .ToList()
                .Select(c => (Character)c).FirstOrDefault();
            

            if (character != null)
            {
                ctx.Options
                    .Where(o => o.Owner == characterId)
                    .ToList()
                    .All(o =>
                    {
                        character.SetOption(ushort.Parse(o.Key), o.Value);
                        return true;
                    });
            }

            return character;
        }

        public static Character Load(GameDBContext ctx, uint characterId)
        {
            return ctx.Characters
                .Where(c => c.Id == characterId && c.Deleted == 0)
                .ToList()
                .Select(c => (Character)c)
                .FirstOrDefault();
        }

        public void Load()
        {
            var template = CharacterManager.Instance.GetTemplate((byte)Race, (byte)Gender);
            ModelId = template.ModelId;
            BuyBack = new Item[20];
            Slots = new ActionSlot[85];
            for (var i = 0; i < Slots.Length; i++)
                Slots[i] = new ActionSlot();

            Craft = new CharacterCraft(this);

            using (var ctx = new GameDBContext())
            {
                Inventory.Load(ctx);
                Abilities = new CharacterAbilities(this);
                Abilities.Load(ctx);
                Actability = new CharacterActability(this);
                Actability.Load(ctx);
                Skills = new CharacterSkills(this);
                Skills.Load(ctx);
                Appellations = new CharacterAppellations(this);
                Appellations.Load(ctx);
                Portals = new CharacterPortals(this);
                Portals.Load(ctx);
                Friends = new CharacterFriends(this);
                Friends.Load(ctx);
                Blocked = new CharacterBlocked(this);
                Blocked.Load(ctx);
                Quests = new CharacterQuests(this);
                Quests.Load(ctx);
                Mails = new CharacterMails(this);
                Mails.Load(ctx);
                Mates = new CharacterMates(this);
                Mates.Load(ctx);

                var slots = ctx.Characters
                    .Where(c => c.Id == Id && c.AccountId == AccountId)
                    .Select(c => c.Slots)
                    .ToList()
                    .Select(s => (PacketStream)s)
                    .All(s =>
                    {
                        foreach (var slot in Slots)
                        {
                            slot.Type = (ActionSlotType)s.ReadByte();
                            if (slot.Type != ActionSlotType.None)
                                slot.ActionId = s.ReadUInt32();
                        }
                        return true;
                    });
            }
        }

        public bool Save()
        {
            bool result;
            try
            {

                using (var ctx = new GameDBContext())
                {
                    using (var transaction = ctx.Database.BeginTransaction())
                    {
                        ctx.Characters.RemoveRange(
                            ctx.Characters.Where(c => c.Id == Id && c.AccountId == AccountId));
                        ctx.SaveChanges();

                        ctx.Characters.Add(this.ToEntity());

                        foreach (var pair in _options)
                        {
                            ctx.Options.RemoveRange(
                                ctx.Options.Where(o => o.Key == pair.Key.ToString() && o.Value == pair.Value));
                        }
                        ctx.SaveChanges();

                        foreach (var pair in _options)
                        {
                            ctx.Options.Add(new Options()
                            {
                                Key = pair.Key.ToString(),
                                Value = pair.Value,
                                Owner = (int)Id
                            });
                        }
                        ctx.SaveChanges();

                        Inventory?.Save(ctx);
                        Abilities?.Save(ctx);
                        Actability?.Save(ctx);
                        Appellations?.Save(ctx);
                        Portals?.Save(ctx);
                        Friends?.Save(ctx);
                        Blocked?.Save(ctx);
                        Skills?.Save(ctx);
                        Quests?.Save(ctx);
                        Mails?.Save(ctx);
                        Mates?.Save(ctx);

                        try
                        {
                            transaction.Commit();
                            result = true;
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex);

                            try
                            {
                                transaction.Rollback();
                            }
                            catch (Exception ex2)
                            {
                                _log.Error(ex2);
                            }

                            result = false;
                        }
                    }
                }
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

            foreach (var item in Inventory.Equip)
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

        public DB.Game.Characters ToEntity()
        {
            var slots = new PacketStream();
            foreach (var slot in Slots)
            {
                slots.Write((byte)slot.Type);
                if (slot.Type != ActionSlotType.None)
                    slots.Write(slot.ActionId);
            }

            return new Characters()
            {
                Id                  =        Id                                               ,
                AccountId           =        AccountId                                        ,
                Name                =        Name                                             ,
                AccessLevel         =        AccessLevel                                      ,
                Race                = (byte) Race                                             ,
                Gender              = (byte) Gender                                           ,
                Level               =        Level                                            ,
                Expirience          =        Expirience                                       ,
                RecoverableExp      =        RecoverableExp                                   ,
                Hp                  =        Hp                                               ,
                Mp                  =        Mp                                               ,
                LaborPower          =        LaborPower                                       ,
                LaborPowerModified  =        LaborPowerModified                               ,
                ConsumedLp          =        ConsumedLaborPower                               ,
                Ability1            = (byte) Ability1                                         ,
                Ability2            = (byte) Ability2                                         ,
                Ability3            = (byte) Ability3                                         ,
                WorldId             =        (WorldPosition?.WorldId   ?? Position.WorldId)   ,
                ZoneId              =        (WorldPosition?.ZoneId    ?? Position.ZoneId)    ,
                X                   =        WorldPosition ?.X         ?? Position.X          ,
                Y                   =        WorldPosition ?.Y         ?? Position.Y          ,
                Z                   =        WorldPosition ?.Z         ?? Position.Z          ,
                RotationX           =        (WorldPosition?.RotationX ?? Position.RotationX) ,
                RotationY           =        (WorldPosition?.RotationY ?? Position.RotationY) ,
                RotationZ           =        (WorldPosition?.RotationZ ?? Position.RotationZ) ,
                FactionId           =        Faction.Id                                       ,
                FactionName         =        FactionName                                      ,
                ExpeditionId        =        (Expedition?.Id ?? 0)                            ,
                Family              =        Family                                           ,
                DeadCount           =        DeadCount                                        ,
                DeadTime            =        DeadTime                                         ,
                RezPenaltyDuration  =        RezWaitDuration                                  ,
                RezTime             =        RezTime                                          ,
                RezWaitDuration     =        RezPenaltyDuration                               ,
                LeaveTime           =        LeaveTime                                        ,
                Money               =        Money                                            ,
                Money2              =        Money2                                           ,
                HonorPoint          =        HonorPoint                                       ,
                VocationPoint       =        VocationPoint                                    ,
                CrimePoint          =        CrimePoint                                       ,
                CrimeRecord         =        CrimeRecord                                      ,
                DeleteRequestTime   =        DeleteRequestTime                                ,
                TransferRequestTime =        TransferRequestTime                              ,
                DeleteTime          =        DeleteTime                                       ,
                BmPoint             = (int)  BmPoint                                          ,
                AutoUseAapoint      = (byte) (AutoUseAAPoint ? 1 : 0)                         ,
                PrevPoint           =        PrevPoint                                        ,
                Point               =        Point                                            ,
                Gift                =        Gift                                             ,
                NumInvSlot          =        NumInventorySlots                                ,
                NumBankSlot         =        NumBankSlots                                     ,
                ExpandedExpert      =        ExpandedExpert                                   ,
                UpdatedAt           =        Updated                                          ,
                Slots               =        slots.GetBytes()                                 ,
                UnitModelParams     =        ModelParams.Write(new PacketStream()).GetBytes() ,
            };

        }

        public static explicit operator Character(Characters v)
        {
            var stream = (PacketStream)v.UnitModelParams;
            var modelParams = new UnitCustomModelParams();
            modelParams.Read(stream);

            Character c = new Character(modelParams) 
            {
                Position            = new Point()
                                    {
                                        WorldId   = v.WorldId             ,
                                        ZoneId    = v.ZoneId              ,
                                        X         = v.X                   ,
                                        Y         = v.Y                   ,
                                        Z         = v.Z                   ,
                                        RotationX = v.RotationX           ,
                                        RotationY = v.RotationY           ,
                                        RotationZ = v.RotationZ           ,
                                    }                                     ,
                AccountId           =               v.AccountId           ,
                Id                  =               v.Id                  ,
                Name                =               v.Name                ,
                AccessLevel         =               v.AccessLevel         ,
                Race                = (Race)        v.Race                ,
                Gender              = (Gender)      v.Gender              ,
                Level               =               v.Level               ,
                Expirience          =               v.Expirience          ,
                RecoverableExp      =               v.RecoverableExp      ,
                Hp                  =               v.Hp                  ,
                Mp                  =               v.Mp                  ,
                LaborPower          =               v.LaborPower          ,
                LaborPowerModified  =               v.LaborPowerModified  ,
                ConsumedLaborPower  =               v.ConsumedLp          ,
                Ability1            = (AbilityType) v.Ability1            ,
                Ability2            = (AbilityType) v.Ability2            ,
                Ability3            = (AbilityType) v.Ability3            ,
                FactionName         =               v.FactionName         ,
                Family              =               v.Family              ,
                DeadCount           =               v.DeadCount           ,
                DeadTime            =               v.DeadTime            ,
                RezWaitDuration     =               v.RezWaitDuration     ,
                RezTime             =               v.RezTime             ,
                RezPenaltyDuration  =               v.RezPenaltyDuration  ,
                LeaveTime           =               v.LeaveTime           ,
                Money               =               v.Money               ,
                Money2              =               v.Money2              ,
                HonorPoint          =               v.HonorPoint          ,
                VocationPoint       =               v.VocationPoint       ,
                CrimePoint          =               v.CrimePoint          ,
                CrimeRecord         =               v.CrimeRecord         ,
                TransferRequestTime =               v.TransferRequestTime ,
                DeleteRequestTime   =               v.DeleteRequestTime   ,
                DeleteTime          =               v.DeleteTime          ,
                BmPoint             =               v.BmPoint             ,
                AutoUseAAPoint      =               v.AutoUseAapoint == 1 ,
                PrevPoint           =               v.PrevPoint           ,
                Point               =               v.Point               ,
                Gift                =               v.Gift                ,
                NumInventorySlots   =               v.NumInvSlot          ,
                NumBankSlots        =               v.NumBankSlot         ,
                ExpandedExpert      =               v.ExpandedExpert      ,
                Updated             =               v.UpdatedAt           ,
                Faction             = FactionManager.Instance.GetFaction(v.FactionId),
                Expedition          = ExpeditionManager.Instance.GetExpedition(v.ExpeditionId),
            };

            c.Inventory = new Inventory(c);
            c.Hp = Math.Min(c.Hp, c.MaxHp);
            c.Mp = Math.Min(c.Mp, c.MaxMp);
            c.CheckExp();

            return c;
        }
    }
}
