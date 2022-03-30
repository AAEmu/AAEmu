using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadPhaseChangedPacket : GamePacket
    {
        private readonly Doodad _doodad;
        private readonly bool _isGoods;

        public SCDoodadPhaseChangedPacket(Doodad doodad, bool isGoods = false) : base(SCOffsets.SCDoodadPhaseChangedPacket, 5)
        {
            _doodad = doodad;
            _isGoods = isGoods;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_doodad.ObjId);     // objId
            stream.Write(_doodad.FuncGroupId); // funcGroupId
            stream.Write(0u);                  // type
            stream.Write(_doodad.TimeLeft);    // growing
            stream.Write(-1);                  // puzzleGroup
            stream.Write(0u);                  // type(id)
            stream.Write(false);               // isGoods
            if (!_isGoods)
                return stream;

            stream.Write(0UL);                 // freshnessTime
            stream.Write(0u);                  // type
            stream.Write(0u);                  // type

            return stream;
        }
    }
}
