using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAppellationsPacket : GamePacket
    {
        private readonly (uint id, bool active)[] _appellations;

        public SCAppellationsPacket((uint id, bool active)[] appellations) : base(SCOffsets.SCAppellationsPacket, 1)
        {
            _appellations = appellations;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_appellations.Length); // TODO max 512
            foreach (var (id, selected) in _appellations)
            {
                stream.Write(id);
                stream.Write(selected);
            }

            return stream;
        }
    }
}
