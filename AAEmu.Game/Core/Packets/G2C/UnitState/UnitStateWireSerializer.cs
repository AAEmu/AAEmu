using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

/// <summary>
/// nested serializers own their own discriminators, widths, and optional branches.
/// </summary>
public static class UnitStateWireSerializer
{
    public static BaseUnitType GetBaseUnitType(Unit unit) => unit switch
    {
        Character => BaseUnitType.Character,
        Npc => BaseUnitType.Npc,
        Slave => BaseUnitType.Slave,
        House => BaseUnitType.Housing,
        Transfer => BaseUnitType.Transfer,
        Mate => BaseUnitType.Mate,
        Shipyard => BaseUnitType.Shipyard,
        _ => BaseUnitType.Invalid
    };

    public static void Write(PacketStream stream, Unit unit, BaseUnitType baseUnitType)
    {
        var context = new UnitStateWireContext(unit, baseUnitType);

        UnitStateIdentitySerializer.Write(stream, context);
        UnitStatePlacementSerializer.Write(stream, context);
        UnitStateAppearanceSerializer.Write(stream, context);
        UnitStateRelationSerializer.Write(stream, context);
        UnitStateGameplaySerializer.Write(stream, context);
        UnitStateCharacterSerializer.Write(stream, context);
    }
}
