using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSSetAppellationStampPacket() : GamePacket(CSOffsets.CSSetAppellationStampPacket, 1)
{
    public uint AppStampId { get; private set; }

    public override void Read(PacketStream stream)
    {
        AppStampId = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character is null)
            return;

        character.AppellationStampId = AppStampId;
        character.BroadcastPacket(
            new G2C.SCAppellationChangedPacket(
                character.ObjId,
                character.Appellations.ActiveAppellation,
                character.AppellationStampId),
            true);
    }
}
