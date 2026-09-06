using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRequestUIDataPacket() : GamePacket(CSOffsets.CSRequestUIDataPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != 10)
        {
            Logger.Warn("Rejected UI request: remainingBytes={0}", stream.LeftBytes);
            return;
        }

        var uiDataType = stream.ReadUInt16();
        var id = stream.ReadUInt64();
        if (!UiData.IsSupported(uiDataType) || id > uint.MaxValue ||
            !Connection.Characters.TryGetValue((uint)id, out var character))
        {
            Logger.Warn("Rejected UI request: characterId={0}, type={1}", id, uiDataType);
            return;
        }

        Logger.Debug("UI request: characterId={0}, type={1}, remainingBytes={2}",
            id, uiDataType, stream.LeftBytes);
        Connection.SendPacket(new SCResponseUIDataPacket((uint)id, uiDataType, character.GetOption(uiDataType)));
    }
}
