using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDismissExpeditionPacket : GamePacket
    {
        public CSDismissExpeditionPacket() : base(0x00b, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("DismissExpedition");
            // Empty struct
            ExpeditionManager.Instance.Disband(Connection.ActiveChar);
        }
    }
}
