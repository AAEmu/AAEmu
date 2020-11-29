using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class MoneyChange : ItemTask
    {
        private readonly int _amount;

        public MoneyChange(int amount)
        {
            _type = ItemAction.ChangeMoneyAmount;
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
