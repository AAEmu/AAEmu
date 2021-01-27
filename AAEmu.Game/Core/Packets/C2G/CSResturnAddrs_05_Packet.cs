using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSResturnAddrs_05_Packet : GamePacket
    {
        public CSResturnAddrs_05_Packet() : base(CSOffsets.CSResturnAddrs_05_Packet, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var codeBase = stream.ReadUInt32();
            var codeSize = stream.ReadUInt32();
            var fn = stream.ReadUInt32();
            
            _log.Warn("ResturnAddrs, CodeBase: {0}, CodeSize: {1}, Fn: {2}", codeBase, codeSize, fn);
        }
    }
}
