using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class MoneyChangeBank : ItemTask
    {
        private readonly int _amount;

        public MoneyChangeBank(int amount)
        {
            _type = ItemAction.ChangeBankMoneyAmount;
            _amount = amount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(_amount);
            return stream;
        }
    }
}
