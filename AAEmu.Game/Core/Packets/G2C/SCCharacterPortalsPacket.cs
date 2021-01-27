using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterPortalsPacket : GamePacket
    {
        private readonly Portal[] _portals;

        public SCCharacterPortalsPacket(Portal[] portals) : base(SCOffsets.SCCharacterPortalsPacket, 5)
        {
            _portals = portals;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_portals.Length); // count Int32
            foreach (var portal in _portals)
            {
                stream.Write(portal);
            }

            return stream;
        }
    }
}
