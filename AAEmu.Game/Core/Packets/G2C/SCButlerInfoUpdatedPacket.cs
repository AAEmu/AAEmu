using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opcode 0x34B. The 10.0.2.13 serializer <c>FUN_39C885B0</c> treats <paramref name="updatedFlags"/>
/// as a raw signed field while using these verified bits to select the following state groups.
/// </summary>
public class SCButlerInfoUpdatedPacket(
    ushort errorMessage,
    short updatedFlags,
    bool notifyMessage,
    bool resetAllActability,
    IReadOnlyList<ButlerActabilityWire> actabilities,
    IReadOnlyDictionary<sbyte, ulong> permanentDatas,
    uint laborPower,
    ushort lpChargedAmount,
    ushort remainProductionCost,
    string name,
    IReadOnlyDictionary<uint, uint> unitAttributes)
    : GamePacket(SCOffsets.SCButlerInfoUpdatedPacket, 1)
{
    private const ushort ResetAllActabilityFlag = 0x01;
    private const ushort PermanentDatasFlag = 0x02;
    private const ushort LaborPowerFlag = 0x04;
    private const ushort RemainProductionCostFlag = 0x08;
    private const ushort NameFlag = 0x10;
    private const ushort UnitAttributesFlag = 0x20;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(errorMessage);
        stream.Write(updatedFlags);
        stream.Write(notifyMessage);

        var flags = unchecked((ushort)updatedFlags);
        if (HasFlag(flags, ResetAllActabilityFlag))
        {
            stream.Write(resetAllActability);
            ButlerInfoWire.WriteActabilities(stream, actabilities);
        }
        if (HasFlag(flags, PermanentDatasFlag))
            ButlerInfoWire.WritePermanentDatas(stream, permanentDatas);
        if (HasFlag(flags, LaborPowerFlag))
        {
            stream.Write(laborPower);
            stream.Write(lpChargedAmount);
        }
        if (HasFlag(flags, RemainProductionCostFlag))
            stream.Write(remainProductionCost);
        if (HasFlag(flags, NameFlag))
            stream.Write(name);
        if (HasFlag(flags, UnitAttributesFlag))
            ButlerInfoWire.WriteUnitAttributes(stream, unitAttributes);

        return stream;
    }

    private static bool HasFlag(ushort flags, ushort flag) => (flags & flag) != 0;
}
