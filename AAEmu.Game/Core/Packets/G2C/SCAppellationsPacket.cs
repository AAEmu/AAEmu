using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAppellationsPacket : GamePacket
    {
        private readonly (uint id, bool active)[] _appellations;

        public SCAppellationsPacket((uint id, bool active)[] appellations) : base(SCOffsets.SCAppellationsPacket, 5)
        {
            _appellations = appellations;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_appellations.Length); // countApps
            // TODO in 1.2 max 512
            foreach (var (id, selected) in _appellations)
            {
                stream.Write(id);       // type UInt32
                stream.Write(selected); // selected Byte
            }

            return stream;
        }
    }
}
