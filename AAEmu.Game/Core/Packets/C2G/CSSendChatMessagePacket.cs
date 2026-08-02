using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendChatMessagePacket() : GamePacket(CSOffsets.CSSendChatMessagePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Field order taken from the 10.0.2.13 client's own serializer, which names each value:
        //   i8 cliLocale, u64 chat, string target, i8 targetWorldId, string msg, i8 LanguageType,
        //   u32 ability, then the chat-link block.
        // The previous reader took the header as i16+i16+i32 and had no targetWorldId, so it was a
        // byte short before the first string and two before the second: every command arrived as
        // "Error reading string / OverflowException" and never reached CommandManager.
        var cliLocale = stream.ReadByte();
        var chat = stream.ReadUInt64();
        var type = (ChatType)(short)(chat & 0xFFFF);

        var targetName = stream.ReadString();
        var targetWorldId = stream.ReadByte();
        var message = stream.ReadString();
        var languageType = stream.ReadByte();
        var ability = stream.ReadInt32();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        Logger.Info("CSSendChatMessage locale={0} chat=0x{1:X} type={2} target='{3}' worldId={4} msg='{5}' lang={6} ability={7}",
            cliLocale, chat, type, targetName, targetWorldId, message, languageType, ability);

        if (message.StartsWith(CommandManager.CommandPrefix))
        {
            if (CommandManager.Instance.Handle(character, message.Substring(CommandManager.CommandPrefix.Length).Trim(), out _))
                return;
        }

        var minimumLevel = AppConfiguration.Instance.LevelRestrictions.GetChatLevel(type);
        if (character.Level + character.HeirLevel < minimumLevel)
        {
            character.SendErrorMessage(ErrorMessageType.ChatCannotSendSinceLevelLow);
            return;
        }

        // Sidenote: Trino mixed up /faction and /nation back then, it was supposed to be the other way around
        switch (type)
        {
            case ChatType.Whisper: //whisper
                var target = WorldManager.Instance.GetCharacter(targetName);
                if (target == null || !target.IsOnline)
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.WhisperNoTarget);
                }
                else
                if (target.Faction.MotherId != Connection.ActiveChar.Faction.MotherId)
                {
                    // TODO: proper hostile check
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatCannotWhisperToHostile);
                }
                else
                {
                    var packet = new SCChatMessagePacket(ChatType.Whisper, Connection.ActiveChar, message, ability, languageType);
                    target.SendPacket(packet);
                    var packet_me = new SCChatMessagePacket(ChatType.Whispered, target, message, ability, languageType);
                    Connection.SendPacket(packet_me);
                }
                break;
            case ChatType.White: //say
                Connection.ActiveChar.BroadcastPacket(
                    new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType), true);
                break;
            case ChatType.RaidLeader:
            case ChatType.Raid:
                var teamRaid = TeamManager.Instance.GetActiveTeamByUnit(Connection.ActiveChar.Id);

                if (teamRaid != null)
                {
                    if (type == ChatType.RaidLeader && teamRaid.OwnerId != Connection.ActiveChar.Id)
                    {
                        Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotRaidOwner);
                    }
                    else
                    {
                        ChatManager.Instance.GetRaidChat(teamRaid).SendPacket(new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType));
                    }
                }
                else
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInRaid);
                }
                break;
            case ChatType.Party:
                var partyRaid = TeamManager.Instance.GetActiveTeamByUnit(Connection.ActiveChar.Id);
                if (partyRaid != null)
                {
                    ChatManager.Instance.GetPartyChat(partyRaid, Connection.ActiveChar).SendMessage(Connection.ActiveChar, message, ability, languageType);
                }
                else
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInParty);
                }
                break;
            case ChatType.Trade: //trade
            case ChatType.GroupFind: //lfg
            case ChatType.Shout: //shout
                // We use SendPacket here so we can fake our way through the different channel types
                ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId).SendPacket(
                    new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType)
                    );
                break;
            case ChatType.Clan:
                if (Connection.ActiveChar.Expedition != null)
                {
                    ChatManager.Instance.GetGuildChat(Connection.ActiveChar.Expedition).SendMessage(Connection.ActiveChar, message, ability, languageType);
                }
                else
                {
                    // Looks like the client blocks the chat even before it can get to the server, but let's intercept it anyway
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInExpedition);
                }
                break;
            case ChatType.Family:
                if (Connection.ActiveChar.Family > 0)
                {
                    ChatManager.Instance.GetFamilyChat(Connection.ActiveChar.Family).SendMessage(Connection.ActiveChar, message, ability, languageType);
                }
                else
                {
                    // Looks like the client blocks the chat even before it can get to the server, but let's intercept it anyway
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInFamily);
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
                ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).SendMessage(Connection.ActiveChar, message, ability, languageType);
                break;
            case ChatType.Ally: //faction (by current allegiance)
                ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId).SendMessage(Connection.ActiveChar, message, ability, languageType);
                break;
            default:
                Logger.Warn("Unsupported chat type {0} from {1}", type, Connection.ActiveChar.Name);
                break;
        }
    }
}
