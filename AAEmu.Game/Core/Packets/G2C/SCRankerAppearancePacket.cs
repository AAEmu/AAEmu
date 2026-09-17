using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One ranker's look: what the ranking window needs to show a holder it has never met, and what its
/// "view equipped gear" opens.
/// </summary>
/// <remarks>
/// Field order and widths come from the 10.0.2.13 client's serializer: the world and the holder the
/// request named, the holder's level, the unit appearance (a flag word over the 34 equipment slots with a
/// record per occupied slot, then a second flag word), the customisation block behind its own type byte,
/// then two per-slot lists — the reinforcement level and experience of a slot, and the artifact effects
/// that level carries.
/// </remarks>
public class SCRankerAppearancePacket(sbyte worldId, Unit ranker)
    : GamePacket(SCOffsets.SCRankerAppearance, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)worldId);
        stream.Write((long)ranker.Id);
        stream.Write((byte)ranker.Level);

        EquipmentSerializer.Write(stream, ranker, BaseUnitType.Character);
        stream.Write(ranker.ModelParams);

        // The two per-slot lists: a slot's reinforcement level and experience, and the artifact effect a
        // slot's level carries. Neither is sent yet — a holder's look is what this answer is for.
        stream.Write(0u);
        stream.Write(0u);
        return stream;
    }
}
