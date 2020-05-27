using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Chat;
using Microsoft.EntityFrameworkCore;

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
                if (CommandManager.Instance.Handle(DbLoggerCategory.Database.Connection.ActiveChar, message.Substring(CommandManager.CommandPrefix.Length).Trim()))
                    return;
            }

            // Sidenote: Trino mixed up /faction and /nation back then, it was supposed to be the other way around
            switch (type)
            {
                case ChatType.Whisper: //whisper
                    var target = WorldManager.Instance.GetCharacter(targetName);
                    if ((target == null) || (!target.IsOnline))
                    {
                        DbLoggerCategory.Database.Connection.ActiveChar.SendErrorMessage(ErrorMessageType.WhisperNoTarget);
                    }
                    else
                    if (target.Faction.MotherId != DbLoggerCategory.Database.Connection.ActiveChar.Faction.MotherId)
                    {
                        // TODO: proper hostile check
                        DbLoggerCategory.Database.Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatCannotWhisperToHostile);
                    }
                    else
                    {
                        var packet = new SCChatMessagePacket(ChatType.Whisper, DbLoggerCategory.Database.Connection.ActiveChar, message, ability, languageType);
                        target?.SendPacket(packet);
                        var packet_me = new SCChatMessagePacket(ChatType.Whispered, target, message, ability, languageType);
                        DbLoggerCategory.Database.Connection.SendPacket(packet_me);
                    }
                    break;
                case ChatType.White: //say
                    DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(
                        new SCChatMessagePacket(type, DbLoggerCategory.Database.Connection.ActiveChar, message, ability, languageType), true);
                    break;
                case ChatType.RaidLeader:
                case ChatType.Raid:
                    var teamRaid = TeamManager.Instance.GetActiveTeamByUnit(DbLoggerCategory.Database.Connection.ActiveChar.Id);

                    if (teamRaid != null)
                    {
                        if ((type == ChatType.RaidLeader) && (teamRaid.OwnerId != DbLoggerCategory.Database.Connection.ActiveChar.Id))
                        {
                            DbLoggerCategory.Database.Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotRaidOwner);
                        }
                        else
                        {
                            ChatManager.Instance.GetRaidChat(teamRaid).SendPacket(new SCChatMessagePacket(type, DbLoggerCategory.Database.Connection.ActiveChar, message, ability, languageType));
                        }
                    }
                    else
                    {
                        DbLoggerCategory.Database.Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInRaid);
                    }
                    break;
                case ChatType.Party:
                    var partyRaid = TeamManager.Instance.GetActiveTeamByUnit(DbLoggerCategory.Database.Connection.ActiveChar.Id);
                    if (partyRaid != null)
                    {
                        ChatManager.Instance.GetPartyChat(partyRaid,DbLoggerCategory.Database.Connection.ActiveChar).SendMessage(DbLoggerCategory.Database.Connection.ActiveChar, message, ability, languageType);
                    }
                    else
                    {
                        DbLoggerCategory.Database.Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInParty);
                    }
                    break;
                case ChatType.Trade: //trade
                case ChatType.GroupFind: //lfg
                case ChatType.Shout: //shout
                    // We use SendPacket here so we can fake our way through the different channel types
                    ChatManager.Instance.GetZoneChat(DbLoggerCategory.Database.Connection.ActiveChar.Position.ZoneId).SendPacket(
                        new SCChatMessagePacket(type, DbLoggerCategory.Database.Connection.ActiveChar, message, ability, languageType)
                        );
                    break;
                case ChatType.Clan:
                    if (DbLoggerCategory.Database.Connection.ActiveChar.Expedition != null)
                    {
                        ChatManager.Instance.GetGuildChat(DbLoggerCategory.Database.Connection.ActiveChar.Expedition).SendMessage(DbLoggerCategory.Database.Connection.ActiveChar, message, ability, languageType);
                    }
                    else
                    {
                        // Looks like the client blocks the chat even before it can get to the server, but let's intercept it anyway
                        DbLoggerCategory.Database.Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInExpedition);
                    }
                    break;
                    /*
                case ChatType.Judge: 
                    // TODO: Need a check so only defendant and jury can talk here, the client does some checks too, but let's make sure
                    ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).SendPacket(
                        new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType)
                        );
                    break;
                    */
                case ChatType.Region: //nation (birth place/race, includes pirates etc)
                    ChatManager.Instance.GetNationChat(DbLoggerCategory.Database.Connection.ActiveChar.Race).SendMessage(DbLoggerCategory.Database.Connection.ActiveChar, message, ability, languageType);
                    break;
                case ChatType.Ally: //faction (by current allegiance)
                    ChatManager.Instance.GetFactionChat(DbLoggerCategory.Database.Connection.ActiveChar.Faction.MotherId).SendMessage(DbLoggerCategory.Database.Connection.ActiveChar, message, ability, languageType);
                    break;
                default:
                    _log.Warn("Unsupported chat type {0} from {1}", type, DbLoggerCategory.Database.Connection.ActiveChar.Name);
                    break;
            }
        }
    }
}
