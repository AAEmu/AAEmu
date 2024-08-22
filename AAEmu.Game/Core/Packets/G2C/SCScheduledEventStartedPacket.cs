using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCScheduledEventStartedPacket : GamePacket
{
    private readonly uint _id;
    private readonly int _changes;
    private readonly byte _priority;
    private readonly byte _startDate;
    private readonly byte _periodDate;
    private readonly byte _hour;
    private readonly byte _min;
    private readonly int _periodMin;
    private readonly byte _weekDay;
    private readonly string _openMsg;

    public SCScheduledEventStartedPacket() : base(SCOffsets.SCScheduledEventStartedPacket, 5)
    {
        _id = 1;
        _changes = 344066;
        _priority = 1;
        _startDate = 13;
        _periodDate = 15;
        _hour = 0;
        _min = 0;
        _periodMin = 1440;
        _weekDay = 127;
        _openMsg = "exp 500%";
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_id);
        //for (var i = 0; i < 23; i++) // 21 in 1.2, 23 in 5.0
        {
            stream.Write(0);
            stream.Write(500);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(200);
            stream.Write(0);
            stream.Write(200);
            stream.Write(0);
            stream.Write(400);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
        }
        stream.Write(_changes);
        stream.Write(_priority);
        stream.Write(_startDate);
        stream.Write(_periodDate);
        stream.Write(_hour);
        stream.Write(_min);
        stream.Write(_periodMin);
        stream.Write(_weekDay);
        stream.Write(_openMsg);

        return stream;
    }
}
