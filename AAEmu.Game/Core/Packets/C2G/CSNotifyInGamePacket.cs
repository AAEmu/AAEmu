using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNotifyInGamePacket : GamePacket
    {
        public CSNotifyInGamePacket() : base(0x027, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            Connection.ActiveChar.Spawn();
            Connection.ActiveChar.StartRegen();
            _log.Info("NotifyInGame");
        }
    }
}