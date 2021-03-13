﻿using System.Collections.Generic;
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
        private ChatType type;
        private short subType;
        private uint factionId;

        private string targetName;
        private string message;
        private int ability;
        private byte languageType;
        private byte[] linkType = new byte[4];
        private ushort[] start = new ushort[4];
        private ushort[] lenght = new ushort[4];
        private readonly Dictionary<int, byte[]> data = new Dictionary<int, byte[]>();
        
        public CSSendChatMessagePacket() : base(CSOffsets.CSSendChatMessagePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = (ChatType) stream.ReadInt16(); // ChatChannelNo
            var unk1 = stream.ReadInt16();       //chat, subType
            var unk2 = stream.ReadInt32();        //chat, factionId

            var targetName = stream.ReadString(); // target
            var message = stream.ReadString();    // msg
            var languageType = stream.ReadByte();  // LanguageType
            var ability = stream.ReadInt32();       // ability
            for (var i = 0; i < 4; i++)
            {
                linkType[i] = stream.ReadByte(); // linkType

                if (linkType[i] > 0)
                {
                    start[i] = stream.ReadUInt16();
                    lenght[i] = stream.ReadUInt16();
                    switch (linkType[i])
                    {
                        case 1:
                        case 3:
                            data.TryAdd(i, stream.ReadBytes(208)); // data length = 208
                            break;
                        case 4:
                            break;
                    }
                }
            }

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
                    if ((target == null) || (!target.IsOnline))
                    {
                        Connection.ActiveChar.SendErrorMessage(Models.Game.Error.ErrorMessageType.WhisperNoTarget);
                    }
                    else
                    if (target.Faction.MotherId != Connection.ActiveChar.Faction.MotherId)
                    {
                        // TODO: proper hostile check
                        Connection.ActiveChar.SendErrorMessage(Models.Game.Error.ErrorMessageType.ChatCannotWhisperToHostile);
                    }
                    else
                    {
                        var packet = new SCChatMessagePacket(ChatType.Whisper, Connection.ActiveChar, message, ability, languageType);
                        target?.SendPacket(packet);
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
                        if ((type == ChatType.RaidLeader) && (teamRaid.OwnerId != Connection.ActiveChar.Id))
                        {
                            Connection.ActiveChar.SendErrorMessage(Models.Game.Error.ErrorMessageType.ChatNotRaidOwner);
                        }
                        else
                        {
                            ChatManager.Instance.GetRaidChat(teamRaid).SendPacket(new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType));
                        }
                    }
                    else
                    {
                        Connection.ActiveChar.SendErrorMessage(Models.Game.Error.ErrorMessageType.ChatNotInRaid);
                    }
                    break;
                case ChatType.Party:
                    var partyRaid = TeamManager.Instance.GetActiveTeamByUnit(Connection.ActiveChar.Id);
                    if (partyRaid != null)
                    {
                        ChatManager.Instance.GetPartyChat(partyRaid,Connection.ActiveChar).SendMessage(Connection.ActiveChar, message, ability, languageType);
                    }
                    else
                    {
                        Connection.ActiveChar.SendErrorMessage(Models.Game.Error.ErrorMessageType.ChatNotInParty);
                    }
                    break;
                case ChatType.Trade: //trade
                    // TODO ...
                    Connection.ActiveChar.BroadcastPacket(
                        new SCChatMessagePacket(
                            type, Connection.ActiveChar, message, ability, languageType, linkType, start, lenght, data),
                        true);
                    break;
                case ChatType.GroupFind: //lfg
                case ChatType.Shout: //shout
                    // We use SendPacket here so we can fake our way through the different channel types
                    ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Position.ZoneId).SendPacket(
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
                        Connection.ActiveChar.SendErrorMessage(Models.Game.Error.ErrorMessageType.ChatNotInExpedition);
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
                    _log.Warn("Unsupported chat type {0} from {1}", type, Connection.ActiveChar.Name);
                    break;
            }
        }
    }
}
