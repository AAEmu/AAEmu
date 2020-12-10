using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEditorGameModePacket : GamePacket
    {
        public CSEditorGameModePacket() : base(CSOffsets.CSEditorGameModePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var on = stream.ReadBoolean();
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            // TODO ori? // ((int (__stdcall *)(const char *, char *, _DWORD))a2->Reader->field_5C)("ori", v2 + 40, 0);
            
            // "ori" is byte[16]. No clue how to read it or what it contains.

            _log.Debug("EditorGameMode, On: {0}", on);
        }
    }
}
