using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// </summary>
public class CSUIContentTogglePacket() : GamePacket(CSOffsets.CSUIContentTogglePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var content = stream.ReadByte();
        var visible = stream.ReadBoolean();
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        // Persist as a lightweight option key so reloads can restore later if needed.
        character.SetOption((ushort)(0xE400 + content), visible ? "1" : "0");
        Logger.Debug("CSUIContentToggle: {0} content={1} visible={2}", character.Name, content, visible);
    }
}
