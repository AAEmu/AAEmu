using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class AmountUnk02 : ItemTask
    {
        private readonly int _amount;

        public AmountUnk02(int amount)
        {
            _type = 0x11; // 17
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
