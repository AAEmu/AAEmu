using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSMenuListPacket : GamePacket
    {
        private readonly byte _result;
        private readonly (byte mainTab, List<byte> subTabs)[] _tabs;

        public SCICSMenuListPacket(byte result) : base(SCOffsets.SCICSMenuListPacket, 1)
        {
            _result = result;
            _tabs = new (byte mainTab, List<byte> subTabs)[]
            {
                (1, new List<byte> {2, 3}),
                (2, new List<byte> {1, 2, 6}),
                (3, new List<byte> {1, 2, 6}),
                (4, new List<byte> {1, 4}),
                (0, new List<byte>()),
                (0, new List<byte>())
            };
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_result);
            for (var i = 0; i < 6; i++)
            {
                stream.Write(_tabs[i].mainTab); // mainTab
                for (byte j = 1; j <= 7; j++)
                {
                    if (_tabs[i].subTabs.IndexOf(j) > -1)
                        stream.Write(j); // subTab
                    else
                        stream.Write((byte)0);
                }
            }

            return stream;
        }
    }
}
