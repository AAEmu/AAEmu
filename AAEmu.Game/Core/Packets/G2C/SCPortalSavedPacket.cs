using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPortalSavedPacket : GamePacket
    {
        private readonly Portal _portal;
        
        public SCPortalSavedPacket(Portal portal) : base(SCOffsets.SCPortalSavedPacket, 5)
        {
            _portal = portal;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_portal);

            return stream;
        }
    }
}
