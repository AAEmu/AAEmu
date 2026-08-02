using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Requests the Zone-owned costume appearance for a visible mannequin doodad.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// bc bc (3 bytes)
/// </remarks>
public class CSGetDoodadManikinSkin() : GamePacket(CSOffsets.CSGetDoodadManikinSkin, 1)
{
    public override void Read(PacketStream stream)
    {
        var doodadObjId = stream.ReadBc();
        var character = Connection.ActiveChar;
        var doodad = character?.ParentWorld?.GetDoodad(doodadObjId);
        if (doodad is not DoodadCoffer { IsManikin: true } coffer || !coffer.IsVisible)
            return;

        // item choice. Match that scope through the Zone's region-neighbourhood authority.
        if (!WorldManager.GetAround<Doodad>(character).Any(candidate => candidate.ObjId == doodadObjId))
            return;

        character.SendPacket(new SCSetDoodadManikinSkinPacket(coffer));
    }
}
