using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUnMountMatePacket : GamePacket
    {
        public CSUnMountMatePacket() : base(0x0a6, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var ap = stream.ReadByte();
            var reason = stream.ReadByte();
            
            _log.Warn("UnMountMate, TlId: {0}, Ap: {1}, Reason: {2}", tl, ap, reason);
        }
    }
}
