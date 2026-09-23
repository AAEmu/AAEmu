using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Chronicle book "buy episode": asks the server to create this saga group's chronicle info
/// record, which is what makes the group's quests startable. Answered with
/// <c>SCChronicleInfoBuyPacket</c>.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: u32 type.
/// </remarks>
public class CSChronicleInfoBuyPacket() : GamePacket(CSOffsets.CSChronicleInfoBuyPacket, 1)
{
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
    }

    public override void Execute()
    {
        Connection.ActiveChar?.SagaProgress?.HandleChronicleBuy(TypeValue);
    }
}
