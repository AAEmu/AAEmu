using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSelectHighAbilityPacket : GamePacket
    {
        public CSSelectHighAbilityPacket() : base(CSOffsets.CSSelectHighAbilityPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSSelectHighAbilityPacket");
        }
    }
}
