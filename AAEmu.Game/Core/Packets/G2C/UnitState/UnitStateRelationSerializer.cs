using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

/// <summary>Target/vitals, attachment, bonding, and model-posture nested structures.</summary>
internal static class UnitStateRelationSerializer
{
    private const sbyte NoAttachPoint = -1;
    private const sbyte NoBondingPoint = -1;

    public static void Write(PacketStream stream, UnitStateWireContext context)
    {
        stream.WriteBc(context.Unit.UnitStateTargetObjId);
        stream.Write((ulong)Math.Max(0, context.Unit.Hp) * 100UL);
        stream.Write((ulong)Math.Max(0, context.Unit.Mp) * 100UL);

        WriteAttachment(stream, context);
        WriteBonding(stream, context);
        UnitModelPostureSerializer.Write(
            stream, context.Unit, context.Npc?.AnimActionId ?? 0, activateAnimation: true);
    }

    private static void WriteAttachment(PacketStream stream, UnitStateWireContext context)
    {
        var point = AttachPointKind.None;
        GameObject parent = null;

        if (context.Character is not null && IsWireAttachPoint(context.Character.AttachedPoint))
        {
            point = context.Character.AttachedPoint;
            parent = context.Unit.Transform?.Parent?.GameObject;
        }
        else if (context.Unit is Slave { AttachPointId: >= 0 } slave &&
                 IsWireAttachPoint((AttachPointKind)(byte)slave.AttachPointId))
        {
            point = (AttachPointKind)(byte)slave.AttachPointId;
            parent = slave.ParentObj ?? context.Unit.Transform?.Parent?.GameObject;
        }
        else if (context.Unit is Transfer transfer &&
                 IsWireAttachPoint(transfer.AttachPointId) && transfer.BondingObjId != 0)
        {
            point = transfer.AttachPointId;
        }

        if (IsWireAttachPoint(point))
        {
            var parentObjId = parent?.ObjId ?? (context.Unit as Transfer)?.BondingObjId ?? 0;
            if (parentObjId != 0)
            {
                stream.Write(unchecked((sbyte)point));
                stream.WriteBc(parentObjId);
                return;
            }
        }

        stream.Write(NoAttachPoint);
    }

    private static void WriteBonding(PacketStream stream, UnitStateWireContext context)
    {
        if (context.Character?.Bonding is not null)
        {
            stream.Write(context.Character.Bonding);
            return;
        }

        stream.Write(NoBondingPoint);
    }

    private static bool IsWireAttachPoint(AttachPointKind point) =>
        point is not AttachPointKind.None and not AttachPointKind.System;
}
