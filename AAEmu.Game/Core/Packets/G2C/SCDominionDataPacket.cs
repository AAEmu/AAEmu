using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDominionDataPacket : GamePacket
    {
        private readonly DominionData _dominionData;
        private readonly bool _newlyDeclared;
        private readonly bool _finalDataByRequest;

        public SCDominionDataPacket(DominionData dominionData, bool newlyDeclared, bool finalDataByRequest) : base(SCOffsets.SCDominionDataPacket, 5)
        {
            _dominionData = dominionData;
            _newlyDeclared = newlyDeclared;
            _finalDataByRequest = finalDataByRequest;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_dominionData);
            stream.Write(_newlyDeclared);
            stream.Write(_finalDataByRequest);
            return stream;
        }
    }
}
