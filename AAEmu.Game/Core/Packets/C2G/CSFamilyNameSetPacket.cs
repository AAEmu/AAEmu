using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Routes a family-name request to the owner-authority, ticket-cost, and persistence path.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// string name
/// </remarks>
public class CSFamilyNameSetPacket() : GamePacket(CSOffsets.CSFamilyNameSetPacket, 1)
{
    public string Name { get; private set; }

    public override void Read(PacketStream stream)
    {
        Name = stream.ReadString();
        FamilyManager.Instance.SetName(Connection.ActiveChar, Name);
    }
}
