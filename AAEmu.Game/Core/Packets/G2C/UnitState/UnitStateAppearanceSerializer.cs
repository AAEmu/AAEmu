using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

internal static class UnitStateAppearanceSerializer
{
    public static void Write(PacketStream stream, UnitStateWireContext context)
    {
        EquipmentSerializer.Write(stream, context.Unit, context.BaseUnitType);

        var appearance = context.Unit.ModelParams;
        if (context.Character is not null)
        {
            appearance.Race = (byte)context.Character.Race;
            appearance.Gender = (byte)context.Character.Gender;
            appearance.ClearUnusedVisualRaceOverride((byte)context.Character.Race);
        }
        if (context.Npc is not null && appearance.BodyWeight == 0f)
            appearance.BodyWeight = 1f;

        stream.Write(appearance);
    }
}
