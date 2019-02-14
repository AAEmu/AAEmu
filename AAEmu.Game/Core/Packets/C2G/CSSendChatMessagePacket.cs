using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSendChatMessagePacket : GamePacket
    {
        public CSSendChatMessagePacket() : base(0x063, 1) //TODO 1.0 opcode: 0x061
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = (ChatType) stream.ReadInt16();
            var unk1 = stream.ReadInt16();
            var unk2 = stream.ReadInt32();

            var targetName = stream.ReadString();
            var message = stream.ReadString();
            var languageType = stream.ReadByte();
            var ability = stream.ReadInt32();

            if (message.StartsWith("/"))
            {
                CommandManager.Instance.Handle(Connection.ActiveChar, message.Substring(1).Trim());
                return;
            }

            switch (type)
            {
                case ChatType.Whisper:
                    var target = WorldManager.Instance.GetCharacter(targetName);
                    var packet = new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType);
                    target?.SendPacket(packet);
                    Connection.SendPacket(packet);
                    break;
                case ChatType.White:
                    Connection.ActiveChar.BroadcastPacket(
                        new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType), true);
                    break;
                case ChatType.Shout:
                    // TODO ...
                    break;
                case ChatType.Region:
                    WorldManager.Instance.BroadcastPacketToFaction(
                        new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType),
                        Connection.ActiveChar.Faction.Id);
                    break;
                case ChatType.Ally:
                    WorldManager.Instance.BroadcastPacketToNation(
                        new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType), Connection.ActiveChar.Race);
                    break;
            }
        }
    }
}
