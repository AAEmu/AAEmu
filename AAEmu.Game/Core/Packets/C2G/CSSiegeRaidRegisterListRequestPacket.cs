using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for the siege raid-team registration list of the zone group the character is standing in,
/// which is what the registration popup shows.
/// </summary>
/// <remarks>
/// packet has no body, so the zone group comes from the character's own position.
/// </remarks>
public class CSSiegeRaidRegisterListRequestPacket() : GamePacket(CSOffsets.CSSiegeRaidRegisterListRequestPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        SiegeManager.Instance.SendRaidTeamRegisterList(Connection);
    }
}
