using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelTrialPacket : GamePacket
    {
        public CSCancelTrialPacket() : base(CSOffsets.CSCancelTrialPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var trial = stream.ReadUInt32();
            _log.Warn("CancelTrial, Trial: {0}", trial);
        }
    }
}
