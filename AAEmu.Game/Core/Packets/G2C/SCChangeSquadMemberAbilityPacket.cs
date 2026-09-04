using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC 0x318 broadcast: u64 worldCharKey, u8 ability x3 — level-up/ability swaps inside a squad.
/// </summary>
public class SCChangeSquadMemberAbilityPacket(ulong worldCharKey, byte ability1, byte ability2, byte ability3)
    : GamePacket(SCOffsets.SCChangeSquadMemberAbilityPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldCharKey);
        stream.Write(ability1);
        stream.Write(ability2);
        stream.Write(ability3);
        return stream;
    }
}
