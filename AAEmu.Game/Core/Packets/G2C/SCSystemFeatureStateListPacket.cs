using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_SYSTEM_FEATURE_STATE_LIST (915). First packet the reference server pushes after the context reaches
// INGAME (ChangeState 7): a count followed by (featureId u32, state u32) pairs telling the client which gameplay
// systems are active. The client gates several HUD/UI providers on these states; without the list they read
// uninitialized feature data. The reference sends two entries — feature 2 -> state 3, feature 1 -> state 1.
public class SCSystemFeatureStateListPacket() : GamePacket(SCOffsets.SCSystemFeatureStateListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(2u);   // feature count
        stream.Write(2u);   // featureId
        stream.Write(3u);   // state
        stream.Write(1u);   // featureId
        stream.Write(1u);   // state
        return stream;
    }
}
