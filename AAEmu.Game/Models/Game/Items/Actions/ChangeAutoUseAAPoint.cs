using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ChangeAutoUseAAPoint : ItemTask
    {
        private readonly byte _change;

        public ChangeAutoUseAAPoint(byte change)
        {
            _change = change;
            _type = ItemAction.ChangeAutoUseAaPoint; // 18
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(_change);
            return stream;
        }
    }
}
