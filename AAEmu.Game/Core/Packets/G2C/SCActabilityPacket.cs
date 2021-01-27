using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCActabilityPacket : GamePacket
    {
        private readonly bool _last;
        private readonly Actability[] _actabilities;

        public SCActabilityPacket(bool last, Actability[] actabilities) : base(SCOffsets.SCActabilityPacket, 5)
        {
            _last = last;
            _actabilities = actabilities;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_last);
            stream.Write((byte)_actabilities.Length); // TODO in 1.2 max count 100
            foreach (var actability in _actabilities)
            {
                stream.Write(actability.Id);    // action Int32
                stream.Write(actability.Point); // point Int32
                stream.Write(actability.Step);  // step Byte
            }

            return stream;
        }
    }
}
