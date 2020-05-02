using System.Collections.Concurrent;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class ChatChannel
    {
        public ChatType chatType;
        public short subType; // used for things like zonekey for /shout
        public uint faction;
        public List<Character> members;
        public long internalId;
        public string internalName;

        public ChatChannel()
        {
            chatType = ChatType.White;
            subType = 0;
            faction = 0;
            members = new List<Character>();
            internalId = 0;
            internalName = string.Empty;
        }

        public bool JoinChannel(Character character)
        {
            if (!members.Contains(character))
            {
                members.Add(character);
                character.SendPacket(new SCJoinedChatChannelPacket(chatType, subType, faction));
            }

            //character.SendMessage(ChatType.System, "ChatManager.JoinChannel:{0} - {1}", internalId, internalName);

            return true;
        }

        public bool LeaveChannel(Character character)
        {
            //character.SendMessage(ChatType.System, "ChatManager.LeaveChannel:{0} - {1}", internalId, internalName);

            if (members.Contains(character))
            {
                character.SendPacket(new SCLeavedChatChannelPacket(chatType, subType, faction));
                return members.Remove(character);
            }
            return false;
        }

        /// <summary>
        /// Sends a message to all members of the channel
        /// </summary>
        /// <param name="msg">Text to send</param>
        /// <param name="ability"></param>
        /// <param name="languageType"></param>
        /// <returns>Number of members it was sent to</returns>
        public int SendMessage(string msg, int ability = 0, byte languageType = 0)
        {
            var res = 0;
            foreach(var m in members)
            {
                m.SendPacket(new SCChatMessagePacket(chatType, m, msg, ability, languageType));
                res++;
            }
            return res;
        }

        public int SendPacket(GamePacket packet)
        {
            var res = 0;
            foreach (var m in members)
            {
                m.SendPacket(packet);
                res++;
            }
            return res;
        }

    }

    public class ChatManager : Singleton<ChatManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private ChatChannel nullChannel;
        private ConcurrentDictionary<long, ChatChannel> _factionChannels;
        private ConcurrentDictionary<long, ChatChannel> _nationChannels;
        private ConcurrentDictionary<long, ChatChannel> _zoneChannels;
        private ConcurrentDictionary<long, ChatChannel> _partyChannels;
        private ConcurrentDictionary<long, ChatChannel> _raidChannels;
        private ConcurrentDictionary<long, ChatChannel> _guildChannels;
        private ConcurrentDictionary<long, ChatChannel> _familyChannels;

        public ChatManager()
        {
            nullChannel = new ChatChannel() { chatType = ChatType.White, faction = 0, internalName = "Null" };
            _factionChannels = new ConcurrentDictionary<long, ChatChannel>();
            _nationChannels = new ConcurrentDictionary<long, ChatChannel>();
            _zoneChannels = new ConcurrentDictionary<long, ChatChannel>();
            _partyChannels = new ConcurrentDictionary<long, ChatChannel>();
            _raidChannels = new ConcurrentDictionary<long, ChatChannel>();
            _guildChannels = new ConcurrentDictionary<long, ChatChannel>();
            _familyChannels = new ConcurrentDictionary<long, ChatChannel>();
        }

        public void Initialize()
        {
            _log.Info("Initializing Chat Manager...");
            // Create Faction Channels
            AddFactionChannel(148, "Nuia");
            AddFactionChannel(149, "Haranya");
            AddFactionChannel(114, "Pirate");

            AddNationChannel(Race.Nuian, 148, "Nuian-Elf-Dwarf");
            AddNationChannel(Race.Hariharan, 149, "Harani-Firran-Warborn");
        }

        public List<ChatChannel> ListAllChannels()
        {
            var res = new List<ChatChannel>();
            res.Add(nullChannel);
            res.AddRange(_factionChannels.Values);
            res.AddRange(_nationChannels.Values);
            res.AddRange(_zoneChannels.Values);
            res.AddRange(_partyChannels.Values);
            res.AddRange(_raidChannels.Values);
            res.AddRange(_guildChannels.Values);
            res.AddRange(_familyChannels.Values);
            return res;
        }

        public void LeaveAllChannels(Character character)
        {
            foreach (var c in _factionChannels)
                c.Value.LeaveChannel(character);
            foreach (var c in _nationChannels)
                c.Value.LeaveChannel(character);
            foreach (var c in _zoneChannels)
                c.Value.LeaveChannel(character);
            foreach (var c in _partyChannels)
                c.Value.LeaveChannel(character);
            foreach (var c in _raidChannels)
                c.Value.LeaveChannel(character);
            foreach (var c in _guildChannels)
                c.Value.LeaveChannel(character);
            foreach (var c in _familyChannels)
                c.Value.LeaveChannel(character);
        }

        /// <summary>
        /// Removes zone, party, guild, etc channels that have zero members in them to free up space (and Id's)
        /// </summary>
        public int CleanUpChannels()
        {
            var res = 0;
            foreach (var c in _zoneChannels)
                if (c.Value.members.Count <= 0)
                {
                    _zoneChannels.TryRemove(c.Key, out _);
                    res++;
                }
            foreach (var c in _partyChannels)
                if (c.Value.members.Count <= 0)
                {
                    _zoneChannels.TryRemove(c.Key, out _);
                    res++;
                }
            foreach (var c in _raidChannels)
                if (c.Value.members.Count <= 0)
                {
                    _zoneChannels.TryRemove(c.Key, out _);
                    res++;
                }
            foreach (var c in _guildChannels)
                if (c.Value.members.Count <= 0)
                {
                    _zoneChannels.TryRemove(c.Key, out _);
                    res++;
                }
            foreach (var c in _familyChannels)
                if (c.Value.members.Count <= 0)
                {
                    _zoneChannels.TryRemove(c.Key, out _);
                    res++;
                }
            return res;
        }

        private bool AddFactionChannel(uint factionId,string name)
        {
            var channel = new ChatChannel() { chatType = ChatType.Ally, faction = factionId, internalId = factionId, internalName = name };
            return _factionChannels.TryAdd(factionId, channel);
        }

        public ChatChannel GetFactionChat(uint factionMotherId)
        {
            if (_factionChannels.TryGetValue(factionMotherId, out var c))
                return c;
            else
                return nullChannel;
        }

        private bool AddNationChannel(Race race, uint factionDisplayId, string name)
        {
            var mRace = (((byte)race - 1) & 0xFC);
            var channel = new ChatChannel() { chatType = ChatType.Region, faction = factionDisplayId, internalId = mRace, internalName = name };
            return _nationChannels.TryAdd(mRace, channel);
        }

        public ChatChannel GetNationChat(Race race)
        {
            var mRace = (((byte)race - 1) & 0xFC); // some bit magic that makes raceId into some kind of birth continent id
            if (_nationChannels.TryGetValue(mRace, out var channel))
                return channel;
            else
                return nullChannel;
        }

        private bool AddZoneChannel(uint zoneGroupId,string name)
        {
            var channel = new ChatChannel() { chatType = ChatType.Shout, subType = (short)zoneGroupId, internalId = zoneGroupId, internalName = name };
            return _zoneChannels.TryAdd(zoneGroupId, channel);
        }

        public ChatChannel GetZoneChat(uint zoneKey)
        {
            var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
            var zoneGroupId = zone?.GroupId ?? 0;

            // create it if it's not there
            if (!_zoneChannels.ContainsKey(zoneGroupId))
            {
                var zoneGroupName = ZoneManager.Instance.GetZoneGroupById(zoneGroupId)?.Name ?? "ZoneGroup(" + zoneGroupId.ToString() + ")";
                if (!AddZoneChannel(zoneGroupId, zoneGroupName))
                    _log.Error("Failed to create zone chat channel !");
            }

            if (_zoneChannels.TryGetValue(zoneGroupId, out var channel))
            {
                return channel;
            }
            else
            {
                _log.Error("Should not be able to get a null channel from GetZoneChat !");
                return nullChannel;
            }
        }





    }
}
