using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCKickedPacket : GamePacket
    {
        private readonly KickedReason _reason;
        private readonly string _msg;

        public SCKickedPacket(KickedReason reason, string msg) : base(SCOffsets.SCKickedPacket, 1)
        {
            _reason = reason;
            _msg = msg;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_reason);
            stream.Write(_msg);
            return stream;
        }
    }

    public enum KickedReason : byte
    {
        KickDuplicateAccount = 0x0,
        KickByGm = 0x1,
        KickByMaintenance = 0x2,
        KickByInvalidDoodadInteraction = 0x3,
    }
}
