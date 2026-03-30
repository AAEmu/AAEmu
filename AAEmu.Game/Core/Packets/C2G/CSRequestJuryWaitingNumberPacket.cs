using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Game client sends this whenever you hover over the jury "standby" label on your character panel
/// </summary>
public class CSRequestJuryWaitingNumberPacket() : GamePacket(CSOffsets.CSRequestJuryWaitingNumberPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override void Read(PacketStream stream)
    {
        TrialManager.Instance.GetJuryQueueForPlayer(Connection.ActiveChar);
    }
}
