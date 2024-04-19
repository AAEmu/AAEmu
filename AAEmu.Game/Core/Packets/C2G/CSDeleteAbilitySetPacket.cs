using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDeleteAbilitySetPacket : GamePacket
    {
        public CSDeleteAbilitySetPacket() : base(CSOffsets.CSDeleteAbilitySetPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var slotIndex = stream.ReadByte();

            Logger.Debug("CSDeleteAbilitySetPacket");
        }
    }
}
