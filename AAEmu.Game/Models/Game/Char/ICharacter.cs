using System.Drawing;

using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Char;

public interface ICharacter : IUnit
{
    CharacterQuests Quests { get; set; }
    Inventory Inventory { get; set; }
    long Money { get; set; }
    long Money2 { get; set; }
    int HonorPoint { get; set; }
    int VocationPoint { get; set; }
    short CrimePoint { get; set; }
    CharacterMates Mates { get; set; }
    CharacterAppellations Appellations { get; set; }
    CharacterAbilities Abilities { get; set; }
    byte NumInventorySlots { get; set; }
    short NumBankSlots { get; set; }
    public UnitEvents Events { get; }

    void SendMessage(ChatType type, string message, Color? color = null);
    void SendMessage(string message);
    void SendDebugMessage(string message);
    void SendErrorMessage(ErrorMessageType errorMsgType, uint type = 0, bool isNotify = true);
    void ChangeLabor(short change, int actabilityId);
    void AddExp(int expDelta, bool shouldAddAbilityExp);
    public bool ChangeMoney(SlotType typeFrom, SlotType typeTo, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney);
    public void ChangeGamePoints(GamePointKind kind, int change);
    public void SetGrowthRate(float value);
    public void SetLootRate(float value);
    public void SetVocationRate(float value);
    public void SetHonorRate(float value);
    public void SetExpRate(float value);
    public void SetAutoSaveInterval(float value);
    public void SetLogoutMessage(string value);
    public void SetMotdMessage(string value);
    public void SetGeoDataMode(bool value);
}
