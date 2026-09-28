using System.IO;

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
        if (stream.Count - stream.Pos < sizeof(ulong) + sizeof(int))
            throw new InvalidDataException(
                $"CSFamilyChangeMemberRolePacket: body is {stream.Count - stream.Pos} byte(s); expected a member id and a role.");

        TypeValue = stream.ReadUInt64();
        TypeValue2 = stream.ReadInt32();
        if (stream.Overran)
            throw new InvalidDataException("CSFamilyChangeMemberRolePacket: read past the end of the body.");

        if (Connection is not { ActiveChar: not null } connection)
            return;

        if (TypeValue <= uint.MaxValue && TypeValue2 > 0)
            FamilyManager.Instance.ChangeMemberRole(connection.ActiveChar, (uint)TypeValue, (uint)TypeValue2);
    }
}
