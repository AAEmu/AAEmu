using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Result of a survey-form reply (10.0.2.13 <c>SCSurveyFormSavePacket</c>).
/// </summary>
/// <remarks>
/// 10.0.2.13 client serializer field order:
/// u16 ErrorMessage, u32 type, u8 result
/// </remarks>
public class SCSurveyFormSavePacket(ErrorMessageType errorMessage, uint type, byte result)
    : GamePacket(SCOffsets.SCSurveyFormSavePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)errorMessage);
        stream.Write(type);
        stream.Write(result);
        return stream;
    }
}
