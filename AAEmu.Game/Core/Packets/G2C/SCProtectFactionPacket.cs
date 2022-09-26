using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCProtectFactionPacket : GamePacket
    {
        private readonly byte _protectFaction;
        private readonly DateTime _time;
        private readonly int _year;
        private readonly int _month;
        private readonly int _day;
        private readonly int _hour;
        private readonly int _min;

        public SCProtectFactionPacket(byte protectFaction, DateTime time) : base(SCOffsets.SCProtectFactionPacket, 5)
        {
            _protectFaction = protectFaction;
            _time = time;
            _year = time.Year;
            _month = time.Month;
            _day = time.Day;
            _hour = time.Hour;
            _min = time.Minute;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_protectFaction);
            stream.Write(_time);
            stream.Write(_year);
            stream.Write(_month);
            stream.Write(_day);
            stream.Write(_hour);
            stream.Write(_min);
            return stream;
        }
    }
}
