using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Core.Managers;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatManager : Singleton<ChatManager>, IChatManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// nullChannel is used as a fallback channel, do not use directly
    /// </summary>
    private ChatChannel NullChannel { get; }= new() { ChatType = ChatType.White, Faction = 0, InternalName = "Null" };
    private ConcurrentDictionary<FactionsEnum, ChatChannel> FactionChannels { get; } = new();
    private ConcurrentDictionary<long, ChatChannel> NationChannels { get; } = new();
    private ConcurrentDictionary<long, ChatChannel> ZoneChannels { get; } = new();
    private ConcurrentDictionary<long, ChatChannel> PartyChannels { get; } = new();
    private ConcurrentDictionary<long, ChatChannel> RaidChannels { get; }= new();
    private ConcurrentDictionary<FactionsEnum, ChatChannel> GuildChannels { get; }= new();
    private ConcurrentDictionary<long, ChatChannel> FamilyChannels { get; } = new();

    /// <summary>Open one-to-one chat sessions, keyed by the id the clients were given.</summary>
    private ConcurrentDictionary<long, DirectChatSession> DirectChats { get; } = new();

    /// <summary>The same sessions keyed by the ordered pair of character ids, so one pair has one window.</summary>
    private ConcurrentDictionary<long, DirectChatSession> DirectChatsByPair { get; } = new();

    /// <summary>Last accepted one-to-one send per character, for the content-configured interval.</summary>
    private ConcurrentDictionary<uint, DateTime> DirectChatLastSend { get; } = new();

    private long _nextDirectChatId;
    private bool _directChatRateGapLogged;

    /// <summary>
    /// <c>content_configs</c> name for the minimum seconds between two one-to-one messages from
    /// the same character. Shipped 10.0.2.13 content has no row by this name, so the limiter is
    /// off until an operator adds one - see <see cref="IsDirectChatRateLimited"/>.
    /// </summary>
    public const string DirectChatIntervalConfig = "one_and_one_chat_interval";

    /// <summary>
    /// The single server-wide channel (client calls it CSM, command /u).
    /// </summary>
    /// <remarks>
    /// Unlike every other channel this one is not scoped: no faction, no zone, no group. Both
    /// factions share it and on live it even spans servers, so it carries neither a SubType nor a
    /// Faction - the client identifies it by ChatType.User alone.
    /// </remarks>
    private ChatChannel GlobalChannel { get; } = new()
    {
        ChatType = ChatType.Csm, SubType = 0, Faction = 0, InternalId = 0, InternalName = "CSM"
    };

    /// <summary>
    /// Creates default channels
    /// </summary>
    public void Initialize()
    {
        Logger.Info("Initializing Chat Manager...");

        // Create Faction Channels
        _ = AddFactionChannel(FactionsEnum.NuiaAlliance, "Nuia");
        _ = AddFactionChannel(FactionsEnum.HaranyaAlliance, "Haranya");
        _ = AddFactionChannel(FactionsEnum.Pirate, "Pirate");
        // TODO: Player Factions ?

        // Create Nation Channels
        _ = AddNationChannel(Race.Nuian, FactionsEnum.NuiaAlliance, "Nuian-Elf-Dwarf");
        _ = AddNationChannel(Race.Hariharan, FactionsEnum.HaranyaAlliance, "Harani-Firran-Warborn");

        // The global channel exists for the whole server lifetime; everyone joins it at login.
        Logger.Info("Global chat channel '{0}' ready", GlobalChannel.InternalName);

        // Zone, Party/Raid, Guild, Family channels are created on the fly
    }

    /// <summary>
    /// The server-wide channel every character belongs to.
    /// </summary>
    public ChatChannel GetGlobalChat() => GlobalChannel;

    /// <summary>
    /// Used in GM command /testchatchannel list
    /// </summary>
    /// <returns>List of all chat channels currently loaded</returns>
    public List<ChatChannel> ListAllChannels()
    {
        var res = new List<ChatChannel>
        {
            NullChannel
        };
        res.AddRange(FactionChannels.Values);
        res.AddRange(NationChannels.Values);
        res.AddRange(ZoneChannels.Values);
        res.AddRange(PartyChannels.Values);
        res.AddRange(RaidChannels.Values);
        res.AddRange(GuildChannels.Values);
        res.AddRange(FamilyChannels.Values);
        res.Add(GlobalChannel);
        return res;
    }

    /// <summary>
    /// Removes a player from all chat channels
    /// </summary>
    /// <param name="character"></param>
    public void LeaveAllChannels(Character character)
    {
        foreach (var c in FactionChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in NationChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in ZoneChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in PartyChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in RaidChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in GuildChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in FamilyChannels)
            c.Value?.LeaveChannel(character);
        GlobalChannel.LeaveChannel(character);
    }

    /// <summary>
    /// Removes zone, party, guild, etc. channels that have zero members in them to free up space (and Id's)
    /// </summary>
    public int CleanUpChannels()
    {
        var res = 0;
        foreach (var c in ZoneChannels)
            if (c.Value.MemberCount <= 0)
            {
                ZoneChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in PartyChannels)
            if (c.Value.MemberCount <= 0)
            {
                PartyChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in RaidChannels)
            if (c.Value.MemberCount <= 0)
            {
                RaidChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in GuildChannels)
            if (c.Value.MemberCount <= 0)
            {
                GuildChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in FamilyChannels)
            if (c.Value.MemberCount <= 0)
            {
                FamilyChannels.TryRemove(c.Key, out _);
                res++;
            }
        return res;
    }

    /// <summary>
    /// Creates a faction chat channel
    /// </summary>
    /// <param name="factionId"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    private bool AddFactionChannel(FactionsEnum factionId, string name)
    {
        var channel = new ChatChannel { ChatType = ChatType.Ally, Faction = factionId, InternalId = (uint)factionId, InternalName = name };
        return FactionChannels.TryAdd(factionId, channel);
    }

    /// <summary>
    /// Gets a faction chat channel by FactionId
    /// </summary>
    /// <param name="factionMotherId"></param>
    /// <returns></returns>
    public ChatChannel GetFactionChat(FactionsEnum factionMotherId)
    {
        if (factionMotherId == FactionsEnum.Invalid)
            return NullChannel;

        return FactionChannels.GetOrAdd(factionMotherId, id => new ChatChannel
        {
            ChatType = ChatType.Ally,
            Faction = id,
            InternalId = (uint)id,
            InternalName = $"Faction {id}"
        });
    }

    /// <summary>
    /// Get a character's faction chat channel
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public ChatChannel GetFactionChat(Character character)
    {
        return GetFactionChat(SocialChatAuthorization.ResolveFactionChatId(character?.Faction));
    }

    public int SendFactionMessage(Character origin, string message, int ability = 0, byte languageType = 0)
    {
        // The channel the character is actually in, not the one its faction id names today: the two
        // only differ while a temporary faction change is in force, and then only the channel is the
        // one the client joined and can read.
        var channel = GetJoinedFactionChat(origin);
        if (!SocialChatAuthorization.CanSendFactionChat(channel, origin))
            return 0;

        return channel.SendMessageWhere(origin, ChatType.Ally, message,
            recipient => SocialChatAuthorization.CanReceiveFactionChat(channel, recipient), ability, languageType);
    }

    /// <summary>The faction channel a character is a member of, or null when it is in none.</summary>
    /// <remarks>
    /// Membership is server-assigned - see <see cref="SocialChatAuthorization.CanSendFactionChat"/> -
    /// so the lookup is by membership rather than by faction id on purpose. A character is in one
    /// faction channel at a time because <see cref="SyncFactionChannel"/> leaves the others.
    /// </remarks>
    public ChatChannel GetJoinedFactionChat(Character character)
    {
        if (character == null)
            return null;

        foreach (var channel in FactionChannels.Values)
        {
            if (channel.Contains(character))
                return channel;
        }

        return null;
    }

    public ChatChannel SyncFactionChannel(Character character)
    {
        var current = GetFactionChat(character);
        foreach (var channel in FactionChannels.Values)
        {
            if (!ReferenceEquals(channel, current))
                channel.LeaveChannel(character);
        }
        current.JoinChannel(character);
        return current;
    }

    /// <summary>
    /// Adds a nation chat channel
    /// </summary>
    /// <param name="race"></param>
    /// <param name="factionDisplayId"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    private bool AddNationChannel(Race race, FactionsEnum factionDisplayId, string name)
    {
        var mRace = ((byte)race - 1) & 0xFC;
        var channel = new ChatChannel { ChatType = ChatType.Region, Faction = factionDisplayId, InternalId = mRace, InternalName = name };
        return NationChannels.TryAdd(mRace, channel);
    }

    /// <summary>
    /// Gets nation chat channel by race
    /// </summary>
    /// <param name="race"></param>
    /// <returns></returns>
    public ChatChannel GetNationChat(Race race)
    {
        // some bit magic that makes raceId into some kind of birth continent id
        // If Fairy (for Nuia) and Returned (for Haranya) are ever added as a different faction, we'll need to go and write some proper code for this
        var mRace = ((byte)race - 1) & 0xFC;
        return NationChannels.GetValueOrDefault(mRace, NullChannel);
    }

    /// <summary>
    /// Gets nation chat channel for a character
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public ChatChannel GetNationChat(Character character)
    {
        return GetNationChat(character.Race);
    }

    /// <summary>
    /// Adds a zone group chat channel
    /// </summary>
    /// <param name="zoneGroupId"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    private bool AddZoneChannel(uint zoneGroupId, string name)
    {
        var channel = new ChatChannel { ChatType = ChatType.Shout, SubType = (short)zoneGroupId, InternalId = zoneGroupId, InternalName = name };
        return ZoneChannels.TryAdd(zoneGroupId, channel);
    }

    /// <summary>
    /// Gets or creates a channel by zone key
    /// </summary>
    /// <param name="zoneKey"></param>
    /// <returns></returns>
    public ChatChannel GetZoneChat(uint zoneKey)
    {
        var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
        var zoneGroupId = zone?.GroupId ?? 0;

        // create it if it's not there
        if (!ZoneChannels.ContainsKey(zoneGroupId))
        {
            var zoneGroupName = ZoneManager.Instance.GetZoneGroupById(zoneGroupId)?.Name ?? "ZoneGroup(" + zoneGroupId.ToString() + ")";
            if (!AddZoneChannel(zoneGroupId, zoneGroupName))
                Logger.Error("Failed to create zone chat channel !");
        }

        if (ZoneChannels.TryGetValue(zoneGroupId, out var channel))
        {
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetZoneChat !");
            return NullChannel;
        }
    }

    /// <summary>
    /// Adds a guild specific chat channel
    /// </summary>
    /// <param name="guild"></param>
    /// <returns></returns>
    private bool AddGuildChannel(Expedition guild)
    {
        var channel = new ChatChannel { ChatType = ChatType.Clan, SubType = (short)guild.Id, InternalId = (uint)guild.Id, InternalName = guild.Name };
        return GuildChannels.TryAdd(guild.Id, channel);
    }

    /// <summary>
    /// Get or create a guild channel 
    /// </summary>
    /// <param name="guild"></param>
    /// <returns></returns>
    public ChatChannel GetGuildChat(Expedition guild)
    {
        // create it if it's not there
        if (!GuildChannels.ContainsKey(guild.Id))
        {
            if (!AddGuildChannel(guild))
                Logger.Error("Failed to create guild chat channel !");
        }

        if (GuildChannels.TryGetValue(guild.Id, out var channel))
        {
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetGuildChat !");
            return NullChannel;
        }
    }

    /// <summary>
    /// Adds a family chat channel
    /// </summary>
    /// <param name="familyId"></param>
    /// <returns></returns>
    private bool AddFamilyChannel(uint familyId)
    {
        var channel = new ChatChannel { ChatType = ChatType.Family, SubType = (short)familyId, InternalId = familyId, InternalName = $"Family {familyId}" };
        return FamilyChannels.TryAdd(familyId, channel);
    }

    /// <summary>
    /// Gets a family chat channel by Id
    /// </summary>
    /// <param name="familyId"></param>
    /// <returns></returns>
    public ChatChannel GetFamilyChat(uint familyId)
    {
        // create it if it's not there
        if (!FamilyChannels.ContainsKey(familyId))
        {
            if (!AddFamilyChannel(familyId))
                Logger.Error("Failed to create family chat channel !");
        }

        if (FamilyChannels.TryGetValue(familyId, out var channel))
        {
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetFamilyChat !");
            return NullChannel;
        }
    }

    /// <summary>
    /// Creates a party chat channel
    /// </summary>
    /// <param name="partyId"></param>
    /// <returns></returns>
    private bool AddPartyChannel(uint partyId)
    {
        var channel = new ChatChannel { ChatType = ChatType.Party, SubType = (short)partyId, InternalId = partyId, InternalName = $"Party({partyId})" };
        return PartyChannels.TryAdd(partyId, channel);
    }

    /// <summary>
    /// Get or Creates a party chat channel for Character myChar
    /// </summary>
    /// <param name="party">Team(raid) you belong</param>
    /// <param name="myChar">You</param>
    /// <returns>ChatChannel based on your position inside a Raid</returns>
    public ChatChannel GetPartyChat(Team party, Character myChar)
    {
        var partyId = party.Id << 6;
        // Find my position inside the raid
        uint partyNumber = 0;
        for (uint i = 0; i < party.Members.Length; i++)
        {
            if (party.Members[i] == null || party.Members[i].Character == null)
                continue;
            if (party.Members[i].Character.Id == myChar.Id)
            {
                partyNumber = i / 5;
                break;
            }
        }
        partyId += partyNumber;

        // create it if it's not there
        if (!PartyChannels.ContainsKey(partyId))
        {
            if (!AddPartyChannel(partyId))
                Logger.Error("Failed to create party chat channel !");
        }

        if (PartyChannels.TryGetValue(partyId, out var channel))
        {
            channel.InternalName = $"Party {partyNumber + 1} of {WorldManager.Instance.GetCharacterById(party.OwnerId)?.Name ?? " ???"}";
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetPartyChat !");
            return NullChannel;
        }
    }

    /// <summary>
    /// Creates a raid chat channel
    /// </summary>
    /// <param name="partyId"></param>
    /// <returns></returns>
    private bool AddRaidChannel(uint partyId)
    {
        var channel = new ChatChannel { ChatType = ChatType.Raid, SubType = (short)partyId, InternalId = partyId, InternalName = $"Raid({partyId})" };
        return RaidChannels.TryAdd(partyId, channel);
    }

    /// <summary>
    /// Get Raid channel for your Team
    /// </summary>
    /// <param name="party"></param>
    /// <returns></returns>
    public ChatChannel GetRaidChat(Team party)
    {
        // create it if it's not there
        if (!RaidChannels.ContainsKey(party.Id))
        {
            if (!AddRaidChannel(party.Id))
                Logger.Error("Failed to create party chat channel !");
        }

        if (RaidChannels.TryGetValue(party.Id, out var channel))
        {
            channel.InternalName = $"Raid of {WorldManager.Instance.GetCharacterById(party.OwnerId)?.Name ?? " ???"}";
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetRaidChat !");
            return NullChannel;
        }
    }

    /// <summary>Opens (or returns) the one-to-one session between two characters and announces it to both.</summary>
    /// <remarks>
    /// The client cannot ask for a session: its only one-to-one request,
    /// CSOneAndOneChatAddMessagePacket, quotes an id the server must have handed out first, so the
    /// server opens one. The trigger is an inference, not a pinned retail exchange - the client
    /// ships an "ignore whisper invitation" option, which only makes sense if a delivered whisper
    /// invites both ends into a window - and announcement is once per pair: a second whisper on
    /// the same conversation must not open a second window.
    /// </remarks>
    /// <returns>The session, or null when the pair cannot be authorized.</returns>
    public DirectChatSession StartDirectChat(Character first, Character second)
    {
        if (first == null || second == null)
            return null;

        if (!SocialChatAuthorization.CanSendDirectChat(first, second))
        {
            Logger.Warn("Refusing one-to-one chat start between {0} and {1}",
                first.Name, second.Name);
            return null;
        }

        var pairKey = DirectChatPairKey(first.Id, second.Id);
        if (DirectChatsByPair.TryGetValue(pairKey, out var open))
            return open;

        var created = new DirectChatSession
        {
            Id = Interlocked.Increment(ref _nextDirectChatId),
            CharacterA = first,
            CharacterB = second
        };
        if (!DirectChatsByPair.TryAdd(pairKey, created))
            return DirectChatsByPair.GetValueOrDefault(pairKey);

        DirectChats[created.Id] = created;
        first.SendPacket(new SCOneAndOneChatStartPacket(created.Id, second.Name));
        second.SendPacket(new SCOneAndOneChatStartPacket(created.Id, first.Name));
        return created;
    }

    /// <summary>One key for a pair, independent of which side asked first.</summary>
    private static long DirectChatPairKey(uint firstId, uint secondId)
    {
        var low = Math.Min(firstId, secondId);
        var high = Math.Max(firstId, secondId);
        return ((long)low << 32) | high;
    }

    /// <summary>Delivers one one-to-one message on an already-open session.</summary>
    /// <remarks>
    /// Offline ends are dropped rather than parked: the shipped database has no one-to-one chat
    /// or chat-log table to park into, mail is a separate system with its own types, and the
    /// client keeps window history in its own memory only.
    /// </remarks>
    /// <param name="sender">The character that sent the message.</param>
    /// <param name="chatId">The session id quoted back from the client.</param>
    /// <param name="message">The already length-checked message text.</param>
    /// <returns>How many characters received it (the peer plus the sender's own echo), or 0.</returns>
    public int SendDirectChatMessage(Character sender, long chatId, string message)
    {
        if (sender == null)
        {
            Logger.Error("One-to-one chat message without a sender (chat={0})", chatId);
            return 0;
        }

        if (!DirectChats.TryGetValue(chatId, out var session))
        {
            Logger.Error("One-to-one chat message from {0} quotes unknown session {1}",
                sender.Name, chatId);
            return 0;
        }

        var peer = session.PeerOf(sender);
        if (peer == null)
        {
            Logger.Error("One-to-one chat message from {0} quotes session {1} it is not part of",
                sender.Name, chatId);
            return 0;
        }

        if (!sender.IsOnline || !peer.IsOnline)
        {
            // Dropped, not parked - see SendDirectChatMessage remarks.
            sender.SendErrorMessage(ErrorMessageType.WhisperNoTarget);
            return 0;
        }

        if (!SocialChatAuthorization.CanSendDirectChat(sender, peer))
        {
            sender.SendErrorMessage(ErrorMessageType.ChatCannotWhisperToHostile);
            return 0;
        }

        if (IsDirectChatRateLimited(sender, out var intervalSeconds))
        {
            Logger.Warn("One-to-one chat message from {0} dropped: content_configs '{1}' = {2}s",
                sender.Name, DirectChatIntervalConfig, intervalSeconds);
            return 0;
        }

        var isSpeakerGm = sender.Connection?.GetAttribute("gmFlag") != null;
        var packet = new SCOneAndOneChatAddMessagePacket(chatId, sender.Name, message, isSpeakerGm);
        peer.SendPacket(packet);
        // The window shows nothing for text its own user typed until the server echoes it back.
        sender.SendPacket(packet);
        DirectChatLastSend[sender.Id] = ServerCalendar.UtcNow;
        return 2;
    }

    /// <summary>Tears down every one-to-one session a character is in, plus its rate-limit stamp.</summary>
    /// <returns>How many sessions were removed.</returns>
    public int CloseDirectChatSessions(Character character)
    {
        if (character == null)
            return 0;

        var removed = 0;
        foreach (var pair in DirectChats)
        {
            if (!pair.Value.Involves(character.Id) || !DirectChats.TryRemove(pair.Key, out var session))
                continue;

            DirectChatsByPair.TryRemove(DirectChatPairKey(session.CharacterA.Id, session.CharacterB.Id), out _);
            removed++;
        }

        DirectChatLastSend.TryRemove(character.Id, out _);
        return removed;
    }

    /// <summary>
    /// True when this send has to be dropped because it is inside the content-configured interval.
    /// </summary>
    /// <remarks>
    /// The interval comes from a <c>content_configs</c> row and nowhere else. Shipped content
    /// ships no such row, so there is nothing to fall back to: the limiter is simply off and the
    /// gap is logged once, loudly, for whoever runs the server - never a made-up number. A row
    /// present but zero or negative means the operator turned it off explicitly.
    /// </remarks>
    private bool IsDirectChatRateLimited(Character sender, out long intervalSeconds)
    {
        if (!ContentConfigGameData.Instance.TryGet(DirectChatIntervalConfig, out intervalSeconds))
        {
            if (!_directChatRateGapLogged)
            {
                _directChatRateGapLogged = true;
                Logger.Warn(
                    "content_configs has no '{0}' row: one-to-one chat rate limiting is OFF on this server",
                    DirectChatIntervalConfig);
            }

            return false;
        }

        if (intervalSeconds <= 0)
            return false;

        if (!DirectChatLastSend.TryGetValue(sender.Id, out var last))
            return false;

        return ServerCalendar.UtcNow - last < TimeSpan.FromSeconds(intervalSeconds);
    }
}
