using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Sent by TerminateBeautyShop, by PayBeautyShop when
/// its race check fails, and by the client itself right after it applies
/// SCCharacterGenderAndModelModified while inside the shop. The client leaves shop
/// mode only when SCToggleBeautyshopResponse false comes back, which clears the flag.
/// </summary>
/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so a
/// shared serializer here is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSLeaveBeautyshopPacket() : GamePacket(CSOffsets.CSLeaveBeautyshopPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        CharacterManager.Instance.LeaveBeautyshop(character);
    }
}
