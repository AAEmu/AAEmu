using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public abstract class ItemTask : PacketMarshaler
    {
        protected ItemAction _type;

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_type);
            return stream;
        }
    }

    public enum ItemAction
    {
        Invalid = 0,
        ChangeMoneyAmount = 1,
        ChangeBankMoneyAmount = 2,
        ChangeGamePoint = 3,
        AddStack = 4,
        Create = 5,
        Take = 6,
        Remove = 7,
        SwapSlot = 8,
        UpdateDetail = 9,
        SetFlagsBits = 10,
        UpdateFlags = 11,
        RemoveCrafting = 12,
        Seize = 13,
        ChangeGrade = 14,
        ChangeOwner = 15,
        ChangeAaPoint = 16,
        ChangeBankAaPoint = 17,
        ChangeAutoUseAaPoint = 18,
        UpdateChargeUseSkillTime = 19,
    }
}
