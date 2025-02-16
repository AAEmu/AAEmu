using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCReputationChangedPacket : GamePacket
{
    private readonly DateTime _reputationUpdated;
    private readonly bool _weeklyReset;

    public SCReputationChangedPacket(DateTime reputationUpdated, bool weeklyReset) : base(SCOffsets.SCReputationChangedPacket, 5)
    {
        _reputationUpdated = reputationUpdated;
        _weeklyReset = weeklyReset;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_reputationUpdated);
        stream.Write(_weeklyReset);

        return stream;
    }
}
