using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHousingAreaConfig : GamePacket
{
    private readonly bool _protectOwner;
    private readonly DateTime _time;
    private readonly int _year;
    private readonly int _month;
    private readonly int _day;
    private readonly int _hour;
    private readonly int _min;

    public SCHousingAreaConfig(bool protectOwner, DateTime time) : base(SCOffsets.SCHousingAreaConfig, 5)
    {
        _protectOwner = protectOwner;
        _time = time;
        _year = time.Year;
        _month = time.Month;
        _day = time.Day;
        _hour = time.Hour;
        _min = time.Minute;
    }

    public override PacketStream Write(PacketStream stream)
    {
        #region housingAreaConfig
        stream.Write(1u); // size
        stream.Write(0u); // k
        #region v 
        stream.Write(_protectOwner); // protectOwner
        stream.Write(_time);
        stream.Write(_year);
        stream.Write(_month);
        stream.Write(_day);
        stream.Write(_hour);
        stream.Write(_min);
        #endregion v
        #endregion housingAreaConfig
        return stream;
    }
}
