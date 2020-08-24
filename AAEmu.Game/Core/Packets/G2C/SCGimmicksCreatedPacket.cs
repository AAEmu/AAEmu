using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGimmicksCreatedPacket : GamePacket
    {
        private readonly Gimmick[] _gimmick;

        public SCGimmicksCreatedPacket(Gimmick[] gimmick) : base(SCOffsets.SCGimmicksCreatedPacket, 1)
        {
            _gimmick = gimmick;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_gimmick.Length); // TODO max length 30
            foreach (var gimmick in _gimmick)
            {
                gimmick.Write(stream);
            }

            return stream;
        }
    }
}
