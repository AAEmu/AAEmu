using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class MoneyChangeBank : ItemTask
    {
        private int _amount { get; set; }

        public MoneyChangeBank(int amount)
        {
            _type = 2;
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