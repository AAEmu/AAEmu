using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionDismissedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly bool _success;

        public SCExpeditionDismissedPacket(uint id, bool success) : base(SCOffsets.SCExpeditionDismissedPacket, 5)
        {
            _id = id;
            _success = success;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_success);
            return stream;
        }
    }
}
