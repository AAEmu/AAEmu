using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using NLog;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Client asks to buy/add an extra daily visit (ticket / reset item path).
/// Retail body: u8 visitType + u32 type + u16 type2 (IVT_RESET=3, IVT_PERMIT=4).
/// </summary>
public class CSAddInstanceVisitCountPacket() : GamePacket(CSOffsets.CSAddInstanceVisitCountPacket, 1)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public sbyte VisitType { get; private set; }
    public int TypeValue { get; private set; }
    public short TypeValue2 { get; private set; }

    public override void Read(PacketStream stream)
    {
        VisitType = stream.ReadSByte();
        TypeValue = stream.ReadInt32();
        TypeValue2 = stream.ReadInt16();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        if (!IndunManager.Instance.TryAddInstanceVisitCount(character, VisitType, TypeValue, TypeValue2))
        {
            Logger.Debug(
                "CSAddInstanceVisitCount refused visitType={0} type={1} type2={2} character={3}",
                VisitType, TypeValue, TypeValue2, character.Id);
        }
    }
}

