using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestExpandAbilitySetSlotPacket : GamePacket
    {
        public CSRequestExpandAbilitySetSlotPacket() : base(CSOffsets.CSRequestExpandAbilitySetSlotPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRequestExpandAbilitySetSlotPacket");
        }
    }
}
