using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The in-game shop's "enter beauty shop" request: X2InGameShop.EnterBeautyShop opens
/// EnterBeautyShopDlgTask and its confirm sends this
/// with gender false once the client's own gate passes. The one field is the
/// serializer's bool "gender".
/// </summary>
public class CSBeautyshopBypassPacket() : GamePacket(CSOffsets.CSBeautyshopBypassPacket, 1)
{
    public bool Gender { get; private set; }

    public override void Read(PacketStream stream)
    {
        Gender = stream.ReadBoolean();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        CharacterManager.Instance.EnterBeautyshop(character, Gender);
    }
}
