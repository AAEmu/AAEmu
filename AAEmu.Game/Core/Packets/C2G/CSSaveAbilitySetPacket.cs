using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSaveAbilitySetPacket : GamePacket
    {
        public CSSaveAbilitySetPacket() : base(CSOffsets.CSSaveAbilitySetPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSSaveAbilitySetPacket");
        }
    }
}
