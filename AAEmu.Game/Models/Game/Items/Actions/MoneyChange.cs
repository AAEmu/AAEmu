using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class MoneyChange : ItemTask
    {
        private int _amount { get; set; }

        public MoneyChange(int amount)
        {
            _type = 1;
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