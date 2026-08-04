using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

internal static class UnitStateIdentitySerializer
{
    public static void Write(PacketStream stream, UnitStateWireContext context)
    {
        var unit = context.Unit;
        stream.WriteBc(unit.ObjId);
        stream.Write(unit.Name ?? string.Empty);
        stream.Write(unit.UnitStateWorldId);
        stream.Write(unit.UnitStateRegionId);
        stream.Write(unit.IsInGlobalWorld);

        stream.Write((byte)context.BaseUnitType);
        WriteTypeIdentity(stream, context);

        var masterName = unit.OwnerId > 0
            ? NameManager.Instance.GetCharacterName(unit.OwnerId) ?? string.Empty
            : string.Empty;
        stream.Write(masterName);
    }

    private static void WriteTypeIdentity(PacketStream stream, UnitStateWireContext context)
    {
        switch (context.BaseUnitType)
        {
            case BaseUnitType.Character:
                stream.Write((ulong)(context.Character?.Id ?? 0));
                stream.Write(context.Unit.UnitStateIdentityValue);
                break;
            case BaseUnitType.Npc:
                stream.WriteBc(context.Npc!.ObjId);
                stream.Write(unchecked((int)context.Npc.TemplateId));
                stream.Write((ulong)context.Npc.OwnerId);
                stream.Write(unchecked((sbyte)context.Npc.UnitStateFlag));
                break;
            case BaseUnitType.Slave:
                var slave = (Slave)context.Unit;
                stream.Write((ulong)slave.Id);
                stream.Write(unchecked((short)slave.TlId));
                stream.Write(unchecked((int)slave.TemplateId));
                stream.Write((ulong)(slave.Summoner?.Id ?? 0));
                stream.Write(slave.MasterWorldId);
                break;
            case BaseUnitType.Housing:
                var house = (House)context.Unit;
                var totalBuildSteps = house.Template?.BuildSteps.Count ?? 0;
                var completedBuildSteps = house.CurrentStep < 0 ? totalBuildSteps : house.CurrentStep;
                stream.Write(unchecked((short)house.TlId));
                stream.Write(unchecked((int)house.TemplateId));
                stream.Write((ushort)completedBuildSteps);
                break;
            case BaseUnitType.Transfer:
                var transfer = (Transfer)context.Unit;
                stream.Write(unchecked((short)transfer.TlId));
                stream.Write(unchecked((int)transfer.TemplateId));
                break;
            case BaseUnitType.Mate:
                var mate = (Mate)context.Unit;
                stream.Write(unchecked((short)mate.TlId));
                stream.Write(unchecked((int)mate.TemplateId));
                stream.Write((ulong)mate.OwnerId);
                break;
            case BaseUnitType.Shipyard:
                var shipyard = (Shipyard)context.Unit;
                stream.Write((ulong)shipyard.ShipyardData.Id);
                stream.Write(unchecked((int)shipyard.ShipyardData.TemplateId));
                break;
        }
    }
}
