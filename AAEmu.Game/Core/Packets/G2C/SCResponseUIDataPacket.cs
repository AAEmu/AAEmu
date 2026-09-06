using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResponseUIDataPacket(uint characterId, ushort uiDataType, string uiData)
    : GamePacket(SCOffsets.SCResponseUIDataPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        if (!UiData.IsSupported(uiDataType))
            throw new ArgumentOutOfRangeException(nameof(uiDataType));

        if (!UiData.TryEncode(uiData, out var bytes))
        {
            // Keep invalid persisted data intact; only send a safe empty response.
            Logger.Warn("Invalid stored UI data: characterId={0}, type={1}; sending empty data",
                characterId, uiDataType);
        }

        stream.Write((ulong)characterId);
        stream.Write(uiDataType);
        stream.Write((ushort)bytes.Length);
        stream.Write(bytes, false);
        stream.Write((uint)bytes.Length);
        Logger.Debug("UI response: characterId={0}, type={1}, bytes={2}",
            characterId, uiDataType, bytes.Length);
        return stream;
    }
}
