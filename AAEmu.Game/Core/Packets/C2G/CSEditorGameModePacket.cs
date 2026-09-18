using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSEditorGameModePacket() : GamePacket(CSOffsets.CSEditorGameModePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var on = stream.ReadBoolean();
        var x = Helpers.ConvertLongX(stream.ReadInt64());
        var y = Helpers.ConvertLongY(stream.ReadInt64());
        var z = stream.ReadSingle();
        // The client also carries an "ori" field here; it is byte[16] and its contents are unknown.

        Logger.Debug("EditorGameMode, On: {0}", on);
    }
}
