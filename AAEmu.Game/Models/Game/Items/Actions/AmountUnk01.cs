using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class AmountUnk01 : ItemTask
    {
        private readonly byte _kind;
        private readonly int _amount;

        public AmountUnk01(byte kind, int amount)
        {
            _amount = amount;
            _kind = kind;
            _type = 3;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(_kind); // kind
            stream.Write(_amount);
            return stream;
        }
    }
}
