using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPortalDeletedPacket : GamePacket
    {
        private readonly byte _portalType;
        private readonly int _portalId;

        public SCPortalDeletedPacket(byte portalType, int portalId) : base(SCOffsets.SCPortalDeletedPacket, 5)
        {
            _portalType = portalType;
            _portalId = portalId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_portalType);
            stream.Write(_portalId);
            return stream;
        }
    }
}
