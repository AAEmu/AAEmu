using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char
{
    public interface ICharacter : IUnit
    {
        CharacterAbilities Abilities { get; set; }
        AbilityType Ability1 { get; set; }
        AbilityType Ability2 { get; set; }
        AbilityType Ability3 { get; set; }
        int AccessLevel { get; set; }
        uint AccountId { get; set; }
        CharacterActability Actability { get; set; }
        CharacterAppellations Appellations { get; set; }
        bool AutoUseAAPoint { get; set; }
        CharacterBlocked Blocked { get; set; }
        long BmPoint { get; set; }
        BondDoodad Bonding { get; set; }
        uint Breath { get; set; }
        ItemContainer BuyBackItems { get; set; }
        int ConsumedLaborPower { get; set; }
        CharacterCraft Craft { get; set; }
        short CrimePoint { get; set; }
        int CrimeRecord { get; set; }
        short DeadCount { get; set; }
        DateTime DeadTime { get; set; }
        DateTime DeleteRequestTime { get; set; }
        DateTime DeleteTime { get; set; }
        int Dex { get; }
        byte ExpandedExpert { get; set; }
        int Expirience { get; set; }
        string FactionName { get; set; }
        int Fai { get; }
        float FallDamageMul { get; }
        uint Family { get; set; }
        CharacterFriends Friends { get; set; }
        Gender Gender { get; set; }
        int Gift { get; set; }
        uint HonorGainedInCombat { get; set; }
        int HonorPoint { get; set; }
        uint HostileFactionKills { get; set; }
        uint Id { get; set; }
        bool IgnoreSkillCooldowns { get; set; }
        bool InParty { get; set; }
        int Int { get; }
        Inventory Inventory { get; set; }
        bool IsDrowning { get; }
        bool IsInCombat { get; set; }
        bool IsInPostCast { get; set; }
        bool IsOnline { get; set; }
        bool IsUnderWater { get; set; }
        short LaborPower { get; set; }
        DateTime LaborPowerModified { get; set; }
        DateTime LastCast { get; set; }
        DateTime LastCombatActivity { get; set; }
        DateTime LeaveTime { get; set; }
        float LivingPointGain { get; }
        float LivingPointGainMul { get; }
        WorldSpawnPosition LocalPingPosition { get; set; }
        uint LungCapacity { get; }
        CharacterMails Mails { get; set; }
        CharacterMates Mates { get; set; }
        long Money { get; set; }
        long Money2 { get; set; }
        short NumBankSlots { get; set; }
        byte NumInventorySlots { get; set; }
        int Point { get; set; }
        CharacterPortals Portals { get; set; }
        int PrevPoint { get; set; }
        CharacterQuests Quests { get; set; }
        Race Race { get; set; }
        int RecoverableExp { get; set; }
        uint ResurrectHpPercent { get; set; }
        uint ResurrectionDictrictId { get; set; }
        uint ResurrectMpPercent { get; set; }
        uint ReturnDictrictId { get; set; }
        int RezPenaltyDuration { get; set; }
        DateTime RezTime { get; set; }
        int RezWaitDuration { get; set; }
        CharacterSkills Skills { get; set; }
        ActionSlot[] Slots { get; set; }
        int Spi { get; }
        int Sta { get; }
        int Str { get; }
        List<IDisposable> Subscribers { get; set; }
        DateTime TransferRequestTime { get; set; }
        DateTime Updated { get; set; }
        CharacterVisualOptions VisualOptions { get; set; }
        int VocationPoint { get; set; }

        void AddExp(int exp, bool shouldAddAbilityExp);
        bool AddMoney(SlotType moneyLocation, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney);
        void AddVisibleObject(Character character);
        void ChangeGamePoints(GamePointKind kind, int change);
        void ChangeLabor(short change, int actabilityId);
        bool ChangeMoney(SlotType moneylocation, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney);
        bool ChangeMoney(SlotType typeFrom, SlotType typeTo, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney);
        void CheckExp();
        void CheckLevelUp();
        void DoChangeBreath();
        bool ForceDismount(AttachUnitReason reason = AttachUnitReason.PrefabChanged);
        string GetOption(ushort key);
        WeaponWieldKind GetWeaponWieldKind();
        bool IsActivelyHostile(Character target);
        void Load();
        void OnZoneChange(uint lastZoneKey, uint newZoneKey);
        void PushSubscriber(IDisposable disposable);
        void RemoveVisibleObject(Character character);
        void ResetAllSkillCooldowns(bool triggerGcd);
        void ResetSkillCooldown(uint skillId, bool gcd);
        bool Save(MySqlConnection connection, MySqlTransaction transaction);
        bool SaveDirectlyToDatabase();
        void SendErrorMessage(ErrorMessageType errorMsgType, uint type = 0, bool isNotify = true);
        void SendMessage(ChatType type, string message, params object[] parameters);
        void SendMessage(string message, params object[] parameters);
        void SendOption(ushort key);
        void SetAction(byte slot, ActionSlotType type, uint actionId);
        void SetFaction(uint factionId);
        void SetHostileActivity(Character attacker);
        void SetOption(ushort key, string value);
        void SetPirate(bool pirate);
        bool SubtractMoney(SlotType moneyLocation, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney);
        void UpdateGearBonuses(Item itemAdded, Item itemRemoved);
        PacketStream Write(PacketStream stream);
    }
}
