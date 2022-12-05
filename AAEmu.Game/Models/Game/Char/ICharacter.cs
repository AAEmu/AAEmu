using System.Drawing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Char
{
    public interface ICharacter : IUnit
    {
        CharacterQuests Quests { get; set; }
        Inventory Inventory { get; set; }
        long Money { get; set; }
        CharacterMates Mates { get; set; }
        CharacterAppellations Appellations { get; set; }
        CharacterAbilities Abilities { get; set; }
        byte NumInventorySlots { get; set; }
        short NumBankSlots { get; set; }
        void SendMessage(string message, params object[] parameters);
        void SendMessage(Color color, string message, params object[] parameters);
        void SendErrorMessage(ErrorMessageType errorMsgType, uint type = 0, bool isNotify = true);
        void ChangeLabor(short change, int actabilityId);
        void AddExp(int exp, bool shouldAddAbilityExp);
        void UpdateGearBonuses(Item itemAdded, Item itemRemoved);
        public bool ChangeMoney(SlotType typeFrom, SlotType typeTo, int amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney);
        public void ChangeGamePoints(GamePointKind kind, int change);
    }
}
