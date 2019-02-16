using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDeletePortalPacket : GamePacket
    {
        public CSDeletePortalPacket() : base(0x0db, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var portalType = stream.ReadByte();
            var portalId = stream.ReadInt32();

            _log.Debug("DeletePortal, PortalType: {0}, PortalId: {1}", portalType, portalId);

            PortalManager.Instance.DeletePortal(Connection.ActiveChar, portalType, (uint)portalId);
        }
    }
}
