using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Adds houses to the client's owned/access-list state.
/// </summary>
public class SCHouseDataPacket(IEnumerable<House> houses) : GamePacket(SCOffsets.SCHouseDataPacket, 1)
{
    public const int MaxEntries = 20;

    private readonly House[] _houses = Validate(houses);

    private static House[] Validate(IEnumerable<House> houses)
    {
        ArgumentNullException.ThrowIfNull(houses);
        var entries = houses.ToArray();
        if (entries.Length > MaxEntries)
            throw new ArgumentOutOfRangeException(nameof(houses), entries.Length,
                $"{nameof(SCHouseDataPacket)} supports at most {MaxEntries} entries");
        return entries;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_houses.Length);
        foreach (var house in _houses)
        {
            var position = house.Transform.World.Position;
            stream.Write((short)house.TlId);                         // tl (i16)
            stream.Write((ulong)house.OwnerId);                      // ownerId (u64)
            stream.WriteBc(house.ObjId);                             // objId (bc/u24)
            stream.Write((long)house.AccountId);                     // accountId (i64)
            stream.Write(NameManager.Instance.GetCharacterName(house.OwnerId) ?? string.Empty);
            stream.Write((ulong)Helpers.ConvertLongX(position.X));   // WorldPosition.x (u64)
            stream.Write((ulong)Helpers.ConvertLongY(position.Y));   // WorldPosition.y (u64)
            stream.Write(position.Z);                                // WorldPosition.z (f32)
            stream.Write((int)house.TemplateId);                     // houseTemplateId (i32)
            stream.Write((byte)house.Permission);                    // permission (u8)
            stream.Write(house.Name ?? string.Empty);                // houseName
        }
        return stream;
    }
}
