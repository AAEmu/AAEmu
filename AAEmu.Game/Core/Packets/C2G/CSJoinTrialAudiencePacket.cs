using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSJoinTrialAudiencePacket() : GamePacket(CSOffsets.CSJoinTrialAudiencePacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt32();

        Logger.Trace($"JoinTrialAudience, Id: {id}");
        TrialManager.Instance.JoinTrialAudience(Connection.ActiveChar, id);
    }
}
