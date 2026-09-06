using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSaveUIDataPacket() : GamePacket(CSOffsets.CSSaveUIDataPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes < 12)
        {
            Logger.Warn("Rejected truncated UI save: remainingBytes={0}", stream.LeftBytes);
            return;
        }

        var uiDataType = stream.ReadUInt16();
        var id = stream.ReadUInt64();
        var length = stream.ReadUInt16();
        if (!UiData.IsSupported(uiDataType) || id > uint.MaxValue ||
            length > UiData.MaximumBytes || stream.LeftBytes != length ||
            !Connection.Characters.TryGetValue((uint)id, out var character))
        {
            Logger.Warn("Rejected UI save: characterId={0}, type={1}, bytes={2}, remainingBytes={3}",
                id, uiDataType, length, stream.LeftBytes);
            return;
        }

        if (!UiData.TryDecode(stream.ReadBytes(length), out var data))
        {
            Logger.Warn("Rejected UI save text: characterId={0}, type={1}, bytes={2}",
                id, uiDataType, length);
            return;
        }

        var saved = character.TrySaveUiData(uiDataType, data);
        Logger.Debug("UI save: characterId={0}, type={1}, bytes={2}, remainingBytes={3}, persisted={4}",
            id, uiDataType, length, stream.LeftBytes, saved);
    }
}
