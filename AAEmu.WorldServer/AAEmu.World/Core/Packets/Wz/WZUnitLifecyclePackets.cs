using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

// World → Zone unit lifecycle, factions and spawners (opcodes 0x000-0x01F).
// Each body is the dedicate DLL's own serializer for the type, reached through slot 2

/// <summary>
/// string whose native receive buffer is capped at 0xff bytes. Zone echoes the id in ZW 0x001.
/// </summary>
public class WZRunCommandPacket(uint id, string cmd)
    : ZonePacket(WzOpcodes.RunCommand)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(cmd ?? string.Empty);
    }
}

public class WZUnitHungPacket(uint unitId, uint unitId2, uint unitId3)
    : ZonePacket(WzOpcodes.UnitHung)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
        stream.WriteBc(unitId3);
    }
}

public class WZUnitUnhungPacket(uint unitId, uint unitId2, uint reason)
    : ZonePacket(WzOpcodes.UnitUnhung)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
        stream.Write(reason);
    }
}

public class WZCreateSkillControllerPacket(uint unitId, byte scType, bool fallDamageImmune)
    : ZonePacket(WzOpcodes.CreateSkillController)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(scType);
        stream.Write(fallDamageImmune);
    }
}

public class WZSkillControllerStatePacket(uint unitId, byte scType, float length, bool teared, bool cutouted)
    : ZonePacket(WzOpcodes.SkillControllerState)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(scType);
        if (scType == 0)
        {
            stream.Write(length);
            stream.Write(teared);
            stream.Write(cutouted);
        }
    }
}

public class WZFactionCreatedPacket(int typeValue, int typeValue2, string name, ulong ownerId, string ownerName, byte unitOwnerType, byte politicalSystem, long createdTime, bool aggroLink, bool dTarget, byte allowChangeName, long renameTime, bool integrationFaction)
    : ZonePacket(WzOpcodes.FactionCreated)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(name ?? string.Empty);
        stream.Write(ownerId);
        stream.Write(ownerName ?? string.Empty);
        stream.Write(unitOwnerType);
        stream.Write(politicalSystem);
        stream.Write(createdTime);
        stream.Write(aggroLink);
        stream.Write(dTarget);
        stream.Write(allowChangeName);
        stream.Write(renameTime);
        stream.Write(integrationFaction);
    }
}

public class WZFactionSponsorChangedPacket(int typeValue, int typeValue2, string name, ulong ownerId, string ownerName, byte unitOwnerType, byte politicalSystem, long createdTime, bool aggroLink, bool dTarget, byte allowChangeName, long renameTime, bool integrationFaction)
    : ZonePacket(WzOpcodes.FactionSponsorChanged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(name ?? string.Empty);
        stream.Write(ownerId);
        stream.Write(ownerName ?? string.Empty);
        stream.Write(unitOwnerType);
        stream.Write(politicalSystem);
        stream.Write(createdTime);
        stream.Write(aggroLink);
        stream.Write(dTarget);
        stream.Write(allowChangeName);
        stream.Write(renameTime);
        stream.Write(integrationFaction);
    }
}

public class WZFactionDismissedPacket(int typeValue)
    : ZonePacket(WzOpcodes.FactionDismissed)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
    }
}

public class WZFactionSetRelationStatePacket(int typeValue, int typeValue2, byte state, byte nextState, long updateTime, long changeTime)
    : ZonePacket(WzOpcodes.FactionSetRelationState)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(state);
        stream.Write(nextState);
        stream.Write(updateTime);
        stream.Write(changeTime);
    }
}

public class WZUnitFactionChangedPacket(uint unitId, int typeValue, int typeValue2, bool temp)
    : ZonePacket(WzOpcodes.UnitFactionChanged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(temp);
    }
}

public class WZUnitExpeditionChangedPacket(uint unitId, int typeValue, int typeValue2)
    : ZonePacket(WzOpcodes.UnitExpeditionChanged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(typeValue);
        stream.Write(typeValue2);
    }
}

public class WZExpeditionWarStatePacket(int typeValue, int typeValue2, bool start)
    : ZonePacket(WzOpcodes.ExpeditionWarState)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(start);
    }
}

public class WZFactionIndependencePacket(int typeValue, int typeValue2, string name, long cTime)
    : ZonePacket(WzOpcodes.FactionIndependence)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(name ?? string.Empty);
        stream.Write(cTime);
    }
}

public class WZUnitEquipmentsRndAttrUnitModifierAvtivateChangedPacket(uint unitId, long flags, long flags2)
    : ZonePacket(WzOpcodes.UnitEquipmentsRndAttrUnitModifierAvtivateChanged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(flags);
        stream.Write(flags2);
    }
}
