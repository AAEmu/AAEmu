using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class SCAppliedToInstantGamePacket : GamePacket
    {
        public SCAppliedToInstantGamePacket() : base(CSOffsets.SCAppliedToInstantGamePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("SCAppliedToInstantGame");
        }
    }
}
