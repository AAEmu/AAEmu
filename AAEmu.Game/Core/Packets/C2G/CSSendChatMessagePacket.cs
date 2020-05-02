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

            if (message.StartsWith(CommandManager.CommandPrefix))
            {
                if (CommandManager.Instance.Handle(Connection.ActiveChar, message.Substring(CommandManager.CommandPrefix.Length).Trim()))
                    return;
            }

            // Sidenote: Trino mixed up /faction and /nation back then, it was supposed to be the other way around
            switch (type)
            {
                case ChatType.Whisper: //whisper
                    var target = WorldManager.Instance.GetCharacter(targetName);
                    var packet = new SCChatMessagePacket(ChatType.Whisper, Connection.ActiveChar, message, ability, languageType);
                    target?.SendPacket(packet);
                    var packet_me = new SCChatMessagePacket(ChatType.Whispered, target, message, ability, languageType);
                    Connection.SendPacket(packet_me);
                    break;
                case ChatType.White: //say
                    Connection.ActiveChar.BroadcastPacket(
                        new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType), true);
                    break;
                case ChatType.Trade: //trade
                case ChatType.GroupFind: //lfg
                case ChatType.Shout: //shout
                    ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Position.ZoneId).SendPacket(
                        new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType)
                        );
                    break;
                case ChatType.Clan:
                    Connection.ActiveChar.Expedition?.SendPacket(
                        new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType)
                        );
                    break;
                    /*
                case ChatType.Judge: // TODO: Need a check so only defendant and jury can talk here
                    ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).SendPacket(
                        new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType)
                        );
                    break;
                    */
                case ChatType.Region: //nation (birth place/race, includes pirates ect)
                    ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).SendMessage(message, ability, languageType);
                    break;
                case ChatType.Ally: //faction (by current allegiance)
                    ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId).SendMessage(message, ability, languageType);
                    break;
                default:
                    _log.Warn("Unsupported chat type {0} from {1}", type, Connection.ActiveChar.Name);
                    break;
            }
        }
    }
}
