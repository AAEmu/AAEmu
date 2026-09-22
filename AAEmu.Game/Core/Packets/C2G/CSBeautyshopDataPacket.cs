using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The salon's "pay and apply" request. 10.0.2.13 body (x2game-dev.dll serializer FUN_39c77890):
/// CharRace u8, CharGender u8, three i32 item ids (the model view's hair, horn and tail slots, 20, 22
/// and 23), the appearance block shared with CSCreateCharacter (FUN_39399a30), then the ticket item
/// id i32 and "count" u32 the client picked in FUN_396f9110.
/// </summary>
public class CSBeautyshopDataPacket() : GamePacket(CSOffsets.CSBeautyshopDataPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var race = stream.ReadByte();
        var gender = stream.ReadByte();
        var hairItemId = stream.ReadInt32();
        var hornItemId = stream.ReadInt32();
        var tailItemId = stream.ReadInt32();
        var model = new UnitCustomModelParams();
        model.Read(stream);
        var ticketItemId = stream.ReadInt32();
        var ticketCount = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        CharacterManager.Instance.ApplyBeautyshopEdit(character,
            new BeautyshopEditRequest(race, gender, hairItemId, hornItemId, tailItemId, model, ticketItemId, ticketCount));
    }
}
