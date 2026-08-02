using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>WZUnitEquipmentChanged (0x01E) - sync equipment slots into Zone unit interest.</summary>
public class WZUnitEquipmentChangedPacket(uint unitId, byte[] body) : ZonePacket(WzOpcodes.UnitEquipmentChanged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(body);
    }
}

/// <summary>WZBuffCreated (0x03E) - opaque body built by Game (SkillCaster + BuffData).</summary>
public class WZBuffCreatedPacket(byte[] body) : ZonePacket(WzOpcodes.BuffCreated)
{
    protected override void WriteBody(PacketStream stream) => stream.Write(body);
}

public class WZBuffRemovedPacket(uint targetId, uint buffId) : ZonePacket(WzOpcodes.BuffRemoved)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(targetId);
        stream.Write(buffId);
    }
}

public class WZInteractNpcPacket(uint playerId, uint npcId) : ZonePacket(WzOpcodes.InteractNpc)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(playerId);
        stream.WriteBc(npcId);
    }
}

public class WZInteractNpcEndPacket(uint playerId, uint npcId) : ZonePacket(WzOpcodes.InteractNpcEnd)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(playerId);
        stream.WriteBc(npcId);
    }
}

/// <summary>
/// </summary>
public class WZSlaveMasterChangedPacket(uint slaveId, long masterId, byte masterWorldId)
    : ZonePacket(WzOpcodes.SlaveMasterChanged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(slaveId);
        stream.Write(masterId);
        stream.Write(masterWorldId);
    }
}

/// <summary>
/// The point precedes the parent id, and the trailing reason is not optional.
/// </summary>
public class WZUnitAttachedPacket(uint unitId, uint targetId, byte attachPoint, byte reason = 0)
    : ZonePacket(WzOpcodes.UnitAttached)
{
    private const byte NoAttachPoint = 0xFF;

    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(attachPoint);
        if (attachPoint != NoAttachPoint)
            stream.WriteBc(targetId);
        stream.Write(reason);
    }
}

public class WZUnitDetachedPacket(uint unitId, byte reason = 0) : ZonePacket(WzOpcodes.UnitDetached)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(reason);
    }
}

/// <summary>
/// bc unit + UnitState_SerializeBonding + bc root (ProcessPassenger).
/// </summary>
public class WZUnitBondToDoodadPacket(uint unitId, BondDoodad bonding, uint rootObjId)
    : ZonePacket(WzOpcodes.UnitBondToDoodad)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(bonding);
        stream.WriteBc(rootObjId);
    }
}

public class WZUnitUnbondFromDoodadPacket(uint unitId) : ZonePacket(WzOpcodes.UnitUnbondFromDoodad)
{
    protected override void WriteBody(PacketStream stream) => stream.WriteBc(unitId);
}
