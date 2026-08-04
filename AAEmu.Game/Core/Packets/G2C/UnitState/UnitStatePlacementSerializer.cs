using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

/// <summary>World position, scale, level pairs, four slot selectors, and model reference.</summary>
internal static class UnitStatePlacementSerializer
{
    private const byte NpcUnsetSlot = byte.MaxValue;
    private const byte DefaultSlot = 0;

    public static void Write(PacketStream stream, UnitStateWireContext context)
    {
        var unit = context.Unit;
        GroundWorldNpc(unit, context);

        stream.WritePosition(unit.Transform.Local.Position);
        stream.Write(unit.Scale);
        stream.Write(checked((sbyte)unit.Level));
        stream.Write(checked((sbyte)unit.HeirLevel));

        WriteLevelBlock(stream, context);
        WriteSlotSelectors(stream, context.BaseUnitType);
        stream.Write(unit.ModelId);
    }

    private static void GroundWorldNpc(Unit unit, UnitStateWireContext context)
    {
        if (context.Npc is null || context.Npc.IsZoneMirror)
            return;

        var position = unit.Transform.Local.Position;
        var height = WorldManager.Instance.GetReferenceHeight(
            context.Npc, position.X, position.Y, position.Z, unit.Transform.ZoneId);
        unit.Transform.Local.SetHeight(height);
    }

    private static void WriteLevelBlock(PacketStream stream, UnitStateWireContext context)
    {
        // other currently modelled unit types carry their normal level and no second heir value.
        if (context.BaseUnitType == BaseUnitType.Npc)
        {
            stream.Write((sbyte)0);
            stream.Write((sbyte)0);
            return;
        }

        stream.Write(checked((sbyte)context.Unit.Level));
        stream.Write((sbyte)0);
    }

    private static void WriteSlotSelectors(PacketStream stream, BaseUnitType baseUnitType)
    {
        var value = baseUnitType == BaseUnitType.Npc ? NpcUnsetSlot : DefaultSlot;
        for (var index = 0; index < 4; index++)
            stream.Write(value);
    }
}
