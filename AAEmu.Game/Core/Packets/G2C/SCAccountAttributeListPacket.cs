using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Sends the account's active attributes when its game session is established.</summary>
/// <remarks>
/// i64 startDate, i64 endData.
/// </remarks>
public class SCAccountAttributeListPacket : GamePacket
{
    public const int MaxAttributes = 10;

    private readonly IReadOnlyList<AccountAttribute> _attributes;

    public SCAccountAttributeListPacket(IReadOnlyList<AccountAttribute> attributes)
        : base(SCOffsets.SCAccountAttributeListPacket, 1)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (attributes.Count > MaxAttributes)
            throw new ArgumentOutOfRangeException(nameof(attributes), attributes.Count,
                $"The native packet accepts at most {MaxAttributes} account attributes.");

        _attributes = attributes;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)_attributes.Count);
        foreach (var attribute in _attributes)
        {
            stream.Write(checked((byte)(AccountAttributeKind)attribute.KindId));
            stream.Write(attribute.KindValue);
            stream.Write(checked((byte)attribute.WorldId));
            stream.Write(checked((uint)attribute.Count));
            stream.Write(attribute.Starts);
            stream.Write(attribute.Expires);
        }

        return stream;
    }
}
