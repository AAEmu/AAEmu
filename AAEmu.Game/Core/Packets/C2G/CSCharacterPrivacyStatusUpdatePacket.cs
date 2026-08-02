using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// The shipped option script exposes only 0 (off) and 1 (on). The native response handler always
/// adopts the echoed status, even when result is false, so rejected values must echo the stored value.
/// </remarks>
public class CSCharacterPrivacyStatusUpdatePacket() : GamePacket(CSOffsets.CSCharacterPrivacyStatusUpdatePacket, 1)
{
    public CharacterPrivacyStatus Status { get; private set; }

    public override void Read(PacketStream stream)
    {
        Status = (CharacterPrivacyStatus)stream.ReadSByte();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        if (Status is not CharacterPrivacyStatus.Off and not CharacterPrivacyStatus.On)
        {
            character.SendPacket(new SCCharacterPrivacyStatusUpdatePacket(false, character.PrivacyStatus));
            return;
        }

        character.PrivacyStatus = Status;
        character.SendPacket(new SCCharacterPrivacyStatusUpdatePacket(true, Status));
    }
}
