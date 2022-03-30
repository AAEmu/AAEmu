using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRebuildHouseTaxInfoPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly int _dr;
        private readonly uint _count;
        private readonly int _bt;
        private readonly bool _vt;
        private readonly float _pd;
        private readonly int _wp;
        private readonly int _dtr;


        public SCRebuildHouseTaxInfoPacket(ushort tl, int dr, uint count, int bt, bool vt, float pd, int wp, int dtr)
            : base(SCOffsets.SCRebuildHouseTaxInfoPacket, 5)
        {
            _tl = tl;
            _dr = dr;
            _count = count;
            _bt = bt;
            _vt = vt;
            _pd = pd;
            _wp = wp;
            _dtr = dtr;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);    // tl (houseId)
            stream.Write(_dr);    // dr
            stream.Write(_count); // count, не более 100 раз
            for (int i = 0; i < _count; i++)
            {
                stream.Write(_bt);
                stream.Write(_vt);
                stream.Write(_pd);
                stream.Write(_wp);
                stream.Write(_dtr);
            }
            return stream;
        }
    }
}
