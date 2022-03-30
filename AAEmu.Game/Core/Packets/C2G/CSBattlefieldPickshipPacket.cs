using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBattlefieldPickshipPacket : GamePacket
    {
        public CSBattlefieldPickshipPacket() : base(CSOffsets.CSBattlefieldPickshipPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSBattlefieldPickshipPacket");
        }
    }
}
