using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHousingTradeListPacket : GamePacket
    {
        private readonly int _count;
        private readonly bool _first;
        private readonly bool _final;
        private readonly House _house;
        private readonly ushort _tl;
        private readonly uint _id;
        private readonly long _price;
        private readonly Point _pos;
        private readonly int _zoneId;
        private readonly int _category;

        public SCHousingTradeListPacket(int count, bool first, bool final, House house,
            ushort tl, uint id, long price, Point pos, int zoneId, int category)
            : base(SCOffsets.SCHousingTradeListPacket, 5)
        {
            _count = count;
            _first = first;
            _final = final;
            _house = house;
            _tl = tl;
            _id = id;
            _price = price;
            _pos = pos;
            _zoneId = zoneId;
            _category = category;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_count);            // count
            stream.Write(_first);            // first
            stream.Write(_final);            // final
            for (var i = 0; i < _count; i++) // TODO не более 50
            {
                stream.Write(_house.DecoLimit);         // decoLimit
                stream.Write(_house.ExpandedDecoLimit); // expandedDecoLimit
                stream.Write(_house.Template.Name);     // name
                stream.Write(_tl);                      // tl
                stream.Write(_id);                      // type
                stream.Write(_price);                   // price

                stream.Write(_pos.X);                   // pos (Position)
                stream.Write(_pos.Y);                   // 
                stream.Write(_pos.Z);                   // 

                stream.Write(_zoneId);                  // zoneId
                stream.Write(_category);                // category
            }

            return stream;
        }
    }
}
