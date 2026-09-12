using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Moves a registered garden blueprint between the player's bag and the character-owned farmhand.
/// </summary>
/// <remarks>
/// Field order and widths come from the 10.0.2.13 client's serializer. The first location always
/// addresses the player's bag; the second addresses the farmhand even when the move is reversed.
/// </remarks>
public class CSSwapButlerItemPacket() : GamePacket(CSOffsets.CSSwapButlerItemPacket, 1)
{
    private const int BodySize = sizeof(byte) * 4 + sizeof(ulong);

    public byte BagType { get; private set; }
    public byte BagIndex { get; private set; }
    public byte ButlerType { get; private set; }
    public byte ButlerIndex { get; private set; }
    public ulong ButlerItemId { get; private set; }

    // Compatibility aliases for packet-inspection callers written before the contextual locations
    // were identified. They are not directional from/to fields.
    public byte FromType => BagType;
    public byte FromIndex => BagIndex;
    public byte ToType => ButlerType;
    public byte ToIndex => ButlerIndex;

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte butler item-swap body, got {stream.LeftBytes} bytes.");

        BagType = stream.ReadByte();
        BagIndex = stream.ReadByte();
        ButlerType = stream.ReadByte();
        ButlerIndex = stream.ReadByte();
        ButlerItemId = stream.ReadUInt64();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var service = SingletonContainer.ServiceProvider?.GetService<ButlerItemStorageService>();
        if (service == null)
        {
            character.SendPacket(new SCButlerItemSwappedPacket(
                BagType, BagIndex, ButlerType, ButlerIndex, ButlerItemId,
                (ushort)ErrorMessageType.InternalError));
            return;
        }

        var result = service.Swap(
            character, BagType, BagIndex, ButlerType, ButlerIndex, ButlerItemId);
        if (!result.Acknowledged)
            character.SendPacket(new SCButlerItemSwappedPacket(
                BagType, BagIndex, ButlerType, ButlerIndex, ButlerItemId, (ushort)result.Error));
    }
}
