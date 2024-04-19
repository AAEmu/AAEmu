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
            // Empty
            Logger.Debug("CSRequestExpandAbilitySetSlotPacket");
        }
    }
}
