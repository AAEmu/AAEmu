using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRemoveMatePacket : GamePacket
    {
        public CSRemoveMatePacket() : base(CSOffsets.CSRemoveMatePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tlId = stream.ReadUInt16();
            
            // _log.Warn("RemoveMate, TlId: {0}", tlId);
            Connection.ActiveChar.Mates.DespawnMate(tlId);
        }
    }
}
