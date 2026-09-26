using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Carries a developer-only fake sell request. The 10.x body is a type and a signed count; there is
/// no authoritative market mutation to apply, so the request is refused without touching state.
/// </summary>
public class CSFakeSellSpecialtyItemPacket() : GamePacket(CSOffsets.CSFakeSellSpecialtyItemPacket, 1)
{
    public uint TypeValue { get; private set; }
    public int ItemCount { get; private set; }

    public override void Read(PacketStream stream)
    {
        (TypeValue, ItemCount) = ReadBody(stream);
    }

    public override void Execute()
    {
        SpecialtyManager.Instance.RejectFakeSpecialtyOperation(
            Connection?.ActiveChar, SpecialtyFakeOperationKind.Sell, TypeValue, ItemCount);
    }

    internal static (uint TypeValue, int ItemCount) ReadBody(PacketStream stream)
    {
        return (stream.ReadUInt32(), stream.ReadInt32());
    }
}
