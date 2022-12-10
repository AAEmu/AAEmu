using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCriminalLockedPacket : GamePacket
    {
        public CSCriminalLockedPacket() : base(CSOffsets.CSCriminalLockedPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            
            _log.Warn("CriminalLocked, Id: {0}", id);
        }
    }
}
