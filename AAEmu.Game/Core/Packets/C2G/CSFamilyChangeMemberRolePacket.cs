using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Routes a role-change request to the family owner-authority and persistence path.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSFamilyChangeMemberRolePacket() : GamePacket(CSOffsets.CSFamilyChangeMemberRolePacket, 1)
{
    public ulong TypeValue { get; private set; }
    public int TypeValue2 { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt64();
        TypeValue2 = stream.ReadInt32();
        if (TypeValue <= uint.MaxValue && TypeValue2 > 0)
            FamilyManager.Instance.ChangeMemberRole(Connection.ActiveChar, (uint)TypeValue, (uint)TypeValue2);
    }
}
