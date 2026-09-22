using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The in-game shop's "enter beauty shop" request: X2InGameShop.EnterBeautyShop opens
/// EnterBeautyShopDlgTask (x2game-dev.dll FUN_39885a10) and its confirm (FUN_39888e90) sends this
/// with gender false once the client's own gate (FUN_396f6c60) passes. The one field is the
/// serializer's (FUN_39c56a40) bool "gender".
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
