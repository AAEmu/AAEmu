using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// A hero pressing Confirm on the "Issue Mobilization Orders" window.
/// </summary>
/// <remarks>
/// Sent by X2Faction:RequestIssuanceOfMobilizationOrder, which takes the doodad it was opened from
/// (x2ui/mobilizationorder/issuance_of_mobilization_order.lua:28). Opcode 0x026 confirmed in-game.
///
/// The doodad is the assembly point: the window's "Assembly Point" row is filled client-side from it,
/// so taking the destination from the same doodad is what keeps both ends naming the same place.
/// </remarks>
public class CSFactionIssuanceOfMobilizationOrderPacket() : GamePacket(CSOffsets.CSFactionIssuanceOfMobilizationOrderPacket, 1)
{
    public uint Bc { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();
    }

    public override void Execute()
    {
        MobilizationOrderManager.Instance.Issue(Connection?.ActiveChar, Bc);
    }
}
