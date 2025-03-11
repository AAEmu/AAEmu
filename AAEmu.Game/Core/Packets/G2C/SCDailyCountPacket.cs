using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDailyCountPacket : GamePacket
{
    private readonly int _totalCount;
    private readonly int _dailyCount;
    private readonly int _dailyMaxCount;

    public SCDailyCountPacket(int totalCount, int dailyCount, int dailyMaxCount) : base(SCOffsets.SCDailyCountPacket, 5)
    {
        _totalCount = totalCount;
        _dailyCount = dailyCount;
        _dailyMaxCount = dailyMaxCount;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_totalCount);
        stream.Write(_dailyCount);
        stream.Write(_dailyMaxCount);

        return stream;
    }
}
