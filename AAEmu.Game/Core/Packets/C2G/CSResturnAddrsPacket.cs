using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSResturnAddrsPacket : GamePacket
    {
        public CSResturnAddrsPacket() : base(CSOffsets.CSResturnAddrsPacket, 1)
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
