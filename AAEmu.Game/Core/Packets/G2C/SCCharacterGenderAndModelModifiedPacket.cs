using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells everyone in range (and the owner) a character's appearance changed. 10.0.2.13 body:
/// type u64 character id, two i32 item ids, the appearance
/// block, bool genderTransfer. The receiver writes the two ids into
/// equipment records unit+0x1090 and unit+0x1230, which with the 0xd0 record stride from unit+0x50
/// are slots 20 (hair) and 22 (horns); the tail slot is not carried. On the owner's client the
/// handler then sends CSLeaveBeautyshop while shop mode is on, and genderTransfer
/// true also raises its "already gender transferred" flag.
/// </summary>
public class SCCharacterGenderAndModelModifiedPacket(Character character, uint hairItemId, uint hornItemId, bool genderTransfer)
    : GamePacket(SCOffsets.SCCharacterGenderAndModelModifiedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)character.Id);   // type (u64)
        stream.Write((int)hairItemId);       // slot 20
        stream.Write((int)hornItemId);       // slot 22
        stream.Write(character.ModelParams); // appearance block
        stream.Write(genderTransfer);        // genderTransfer
        return stream;
    }
}
