using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char.Static;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDailyResetPacket : GamePacket
    {
        private readonly byte _kind;

        public SCDailyResetPacket(DailyResetKind kind) : base(SCOffsets.SCDailyResetPacket, 5)
        {
            _kind = (byte)kind;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_kind);

            return stream;
        }
    }
}
