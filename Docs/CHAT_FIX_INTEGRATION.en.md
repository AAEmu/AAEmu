# Chat Fix: Integration Package

**Source:** Archeaze (`ArcheageSA/Archeaze`), three consecutive commits from 2026-08-07
**Author:** ZombieCakeZ, Co-authored-by: Evenstone-AA
**Client the measurements were taken against:** ArcheAge **10.0.2.13** (ArcheAge Returns / CN)

> **Deutsche Fassung:** [CHAT_FIX_INTEGRATION.md](CHAT_FIX_INTEGRATION.md)

| # | Commit | Title |
|---|--------|-------|
| 1 | `1b5d6c3fcd1bb5d833eb3debfb5a4dd41ed545f0` | fix(chat): correct the SCChatMessage header, so messages actually display |
| 2 | `bbffa8740566d856416d21f3378207c3d8cbb67e` | fix(chat): announce the zone channel at world entry, and add the global CSM channel |
| 3 | `7fe22ae54d6991a17e4063a9a8f5db1569435337` | fix(chat): CSM is chat type 18, not User (15) |

The three commits sit directly on top of each other (the parent of #1 is `aac433e4`), so the
appendix carries **one** combined patch covering all three.

---

## 1. Summary

Before: **no chat channel showed anything at all.** The pipeline was fine the whole time — the
server received `CSSendChatMessage` and answered with `SCChatMessage`. The client dropped every
single message, silently and without an error.

Four independent causes that masked one another:

1. **The `SCChatMessage` header was split into the wrong fields.** The total size was right
   (26 bytes), the field boundaries were not — the client read `chat` and `type` out of the wrong
   bytes.
2. **`chat` is not a type, it is an 8-byte channel descriptor.** Without `subType` and `factionId`
   the message names a channel the client never joined.
3. **The zone channel was never announced to the client.** Shout, Trade and LFG all ride that one
   channel — exactly the three that stayed silent even after fixes 1 and 2.
4. **CSM is chat type 18, not `User` (15).** Outgoing messages fell through to the
   `Unsupported chat type` default.

Afterwards these work: Say, Whisper, Shout, Trade, LFG, Nation/Region, Faction/Ally, and the global
CSM channel.

---

## 2. IMPORTANT: Scope — which half applies to which client?

This is the decisive question before integrating. **The package splits into two halves with
different validity:**

### Part A — channel logic (client-version independent)

Applies to **any** AAEmu derivative, whatever the client version:

- `ChatChannel.AnnounceTo()` — separate membership from the client's knowledge
- `ChatChannel.SendMessage(origin, **type**, ...)` — overload carrying an explicit message type
- `ChatManager.GetGlobalChat()` / `GlobalChannel` — the server-wide CSM channel
- `SCJoinedChatChannelPacket` — name field
- `ChatType.Csm = 18`
- `CSNotifyInGamePacket` — announce after the fact, join CSM

These are pure server-side logic. They repair a conceptual mistake (membership ≠ the client knows),
not a wire-layout error.

### Part B — wire layout (**10.0.2.13 only**)

- `SCChatMessagePacket.Write()` — the whole 26-byte header

Do **not** take this unverified into a vanilla AAEmu serving the 1.2 client (3.0.3.0, 2017-04-20).
The header is different there, and the old layout is most likely correct for it:

```csharp
// Vanilla AAEmu (client 1.2) — SCChatMessagePacket.Write, for comparison:
stream.Write((short)_type);                              // i16 type
stream.Write((short)(_character?.Faction.Id ?? 0));      // i16 chat
stream.Write((uint)(_character?.Faction.Id ?? 0));       // u32 chat/factionId
stream.WriteBc(_character?.ObjId ?? 0);                  // bc  objId
stream.Write(_character?.Id ?? 0);                       // u32 charId   <-- gone in 10.0.2.13
stream.Write((byte)0);                                   // u8  (languageType, was commented out)
stream.Write(_character != null ? (byte)_character.Race : (byte)0);
stream.Write((uint)(_character?.Faction.Id ?? 0));       // u32 type/factionId
stream.Write(name); stream.Write(_message);
stream.Write(_character != null ? _ability : 0);
stream.Write(0);                                         // i32 option
```

Note that 10.0.2.13 **drops the `charId` field entirely** — identity now lives only in `bc`, the
3-byte compressed ObjId. Applying Part B on a different client without checking this shifts
everything from byte 9 onward.

**Rule of thumb:** Part A always. Part B only if the target project genuinely serves 10.0.2.13 —
otherwise verify against your own sniff or the client's deserializer first.

---

## 3. Prerequisite (NOT contained in the three commits!)

Both parts assume that **`CSSendChatMessagePacket.Read()` has already been corrected**. That fix
comes from an *earlier* commit in the fork and is **not** part of the three linked commits. Without
it commit #3 (`ChatType.Csm`) in particular does nothing, because `type` is read out of the wrong
bytes.

Expected reader (10.0.2.13):

```csharp
public override void Read(PacketStream stream)
{
    // Field order taken from the 10.0.2.13 client's own serializer, which names each value:
    //   i8 cliLocale, u64 chat, string target, i8 targetWorldId, string msg, i8 LanguageType,
    //   u32 ability, then the chat-link block.
    var cliLocale     = stream.ReadByte();
    var chat          = stream.ReadUInt64();
    var type          = (ChatType)(short)(chat & 0xFFFF);   // <-- type lives in the low 16 bits

    var targetName    = stream.ReadString();
    var targetWorldId = stream.ReadByte();
    var message       = stream.ReadString();
    var languageType  = stream.ReadByte();
    var ability       = stream.ReadInt32();
    ...
}
```

For comparison, the vanilla state — one byte short before the first string and two before the
second, which is why every command arrived as `Error reading string` / `OverflowException` and never
reached the `CommandManager`:

```csharp
var type = (ChatType)stream.ReadInt16();
var unk1 = stream.ReadInt16();
var unk2 = stream.ReadInt32();
var targetName = stream.ReadString();   // no targetWorldId!
var message = stream.ReadString();
```

**Check this in the target project first.** If it still carries the vanilla reader and the target
client is 10.0.2.13, that reader has to come along too.

---

## 4. The four causes in detail

### 4.1 The header layout (commit #1)

Read out of the client's own serializer — `SCChatMessage::Read`, VA `0x39C71C30` — which names
every field:

| Field | Type | Size | Offset |
|-------|------|------|--------|
| `cliLocale` | u8 | 1 | 0 |
| `chat` | u64 | 8 | 1 |
| `bc` | raw | 3 | 9 |
| `type` | i64 | 8 | 12 |
| `LanguageType` | u8 | 1 | 20 |
| `CharRace` | u8 | 1 | 21 |
| `type` | u32 | 4 | 22 |
| | | **26** | |

The widths come from the archive's vtable thunks: `+0x90` reads 1 byte, `+0x88` reads 2, `+0x80`
and `+0xA0` read 4, `+0x98` reads 8, `+0x1A0` reads the 3-byte compressed id, and `+0x1A8` /
`+0x1D0` are `ret` stubs that consume nothing.

The previous version was reconstructed from a byte sniff. It got the **size** exactly right — also
26 — but split it as `i16+i16+u32 | bc | u32 | u8 | u8 | u32+u32+u8`. Everything therefore sat one
to five bytes off:

| Field | was | is |
|-------|-----|-----|
| `bc` | 8 | **9** |
| `LanguageType` | 15 | **20** |
| `CharRace` | 16 | **21** |

`name` and `msg` still began at 26, which is why nothing ever desynced or got truncated, and why the
bug looked like silence rather than damage. The client simply read `chat` and `type` out of the
wrong bytes, could not resolve either to a real channel, and threw every message away.

### 4.2 `chat` is the channel descriptor (commit #1)

`chat` is not merely the type but **the same 8-byte descriptor `SCJoinedChatChannel` announces**:

```
chat = type (i16) | subType (i16) << 16 | factionId (u32) << 32
```

`CSSendChatMessage` already decodes it exactly that way on the receiving side. Sending only the type
and leaving `subType`/`faction` at zero names a channel the client never joined — and so loses every
message in every channel scoped by zone, party or faction. Say and whisper survived it only because
their values genuinely are zero.

> **Trap — please do not "clean this up":** A direct broadcast (say, whisper) has *no* channel and
> must send zeros there. Filling the fields in from the sender (`_character.Faction.Id`) names an
> unjoined channel again and swallows the message just as thoroughly. That is why the default is
> `faction ?? 0` and **not** the character's faction — only a real `ChatChannel` ever sets these.

From this follows the second change: channels whose message type differs from their own — Trade, LFG
and Shout all ride the zone channel, Raid and RaidLeader share the raid one — need an overload that
carries the type separately, instead of building the packet themselves and losing the channel
identity.

### 4.3 The zone channel was never announced (commit #2)

After the header fix, say and whisper worked but Shout, Trade and LFG stayed silent. Measured at the
join site:

```
ZoneChat join: zoneId=184 channel='e_falcony_plateau' type=Shout subType=11
               members=1 joined=False
```

The channel resolves perfectly. The character is simply **already a member**:
`Character.OnZoneChange` joins it while the login position is applied
(`CSSelectCharacterPacket` → `OnZoneChange(0, ZoneId)`) — long before the client can take a chat
packet. By the time `CSNotifyInGame` gets there, `JoinChannel` sees an existing member, returns
`false` and announces nothing.

**Membership and the client's knowledge of a channel are two different things**, and `JoinChannel`
conflated them. The client's own channel list confirmed the shape of the problem: it listed
6 (Region), 11 (Judge) and 14 (Ally) but never 1 (Shout).

### 4.4 CSM is type 18 (commit #3)

The global channel first ran as `ChatType.User` (15) — the only entry in the enum that looked like a
user channel, and the one the "CSM/u" label suggested. The client disagrees, plainly:

```
CSSendChatMessage locale=2 chat=0x12 type=18 target='' worldId=255 msg='asd'
Unsupported chat type 18 from Catjackson
```

So the announcement named a channel the client never recognised as CSM, and outgoing messages fell
through to the unsupported-type default.

`chat = 0x12` is the bare type with no `subType` and no faction — exactly how a channel scoped to
nothing should look. That also confirms it is **shared across factions** rather than duplicated per
side.

---

## 5. Changed files

| File | Part | What |
|------|------|------|
| `AAEmu.Game/Core/Packets/G2C/SCChatMessagePacket.cs` | **B** | new header layout, `subType`/`faction` parameters, `WriteChatHeader` removed |
| `AAEmu.Game/Models/Game/Chat/ChatChannel.cs` | A | `AnnounceTo()`, `AnnouncedName`, `SendMessage` overload with type |
| `AAEmu.Game/Models/Game/Chat/ChatType.cs` | A | `Csm = 18` |
| `AAEmu.Game/Core/Managers/ChatManager.cs` | A | `GlobalChannel`, `GetGlobalChat()`, added to list + leave |
| `AAEmu.Game/Core/Managers/IChatManager.cs` | A | `GetGlobalChat()` on the interface |
| `AAEmu.Game/Core/Packets/C2G/CSNotifyInGamePacket.cs` | A | announce after the fact, join CSM |
| `AAEmu.Game/Core/Packets/C2G/CSSendChatMessagePacket.cs` | A | `SendMessage` instead of `SendPacket`, `case ChatType.Csm` |
| `AAEmu.Game/Core/Packets/G2C/SCJoinedChatChannelPacket.cs` | A | optional `name` field |

> **Note on the patch:** Three hunks (`IChatManager.cs`, `CSNotifyInGamePacket.cs`, `ChatType.cs`)
> only change `using` → `﻿using` on the first line. That is nothing but an added UTF-8 BOM and is
> **semantically meaningless** — just ignore it when porting by hand.

---

## 6. Step-by-step integration

### 6.1 `ChatType.cs` — new enum value

```csharp
    Ally = 14,
    User = 15,

    /// <summary>
    /// The server-wide channel the client labels CSM (command /u). Measured from a live client:
    /// it sends type 18 with an otherwise empty channel descriptor (chat = 0x12, no subType, no
    /// faction), which fits a channel scoped to nothing at all.
    /// </summary>
    Csm = 18
```

### 6.2 `SCJoinedChatChannelPacket.cs` — name field

```csharp
public class SCJoinedChatChannelPacket(ChatType type, short subType, FactionsEnum factionId, string name = "")
    : GamePacket(SCOffsets.SCJoinedChatChannelPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((short)type);
        stream.Write(subType);
        stream.Write((uint)factionId);
        // User channels are addressed by name in the UI; the fixed ones carry none.
        stream.Write(name ?? "");
        return stream;
    }
}
```

### 6.3 `ChatChannel.cs` — split the announcement, carry the type

New:

```csharp
/// <summary>Only user channels are addressed by name; the fixed ones are identified by type alone.</summary>
private string AnnouncedName => ChatType is ChatType.User or ChatType.Csm ? InternalName : string.Empty;

/// <summary>
/// Re-announces this channel to a character that is already a member.
/// </summary>
public void AnnounceTo(Character character)
{
    character?.SendPacket(new SCJoinedChatChannelPacket(ChatType, SubType, Faction, AnnouncedName));
}

/// <summary>
/// Sends a message whose ChatType differs from the channel's own
/// (Trade/LFG/Shout on the zone channel, Raid/RaidLeader on the raid channel).
/// </summary>
public int SendMessage(Character origin, ChatType type, string msg, int ability = 0, byte languageType = 0)
{
    var res = 0;
    foreach (var m in Members)
    {
        m.SendPacket(new SCChatMessagePacket(type, origin ?? m, msg, ability, languageType, SubType, Faction));
        res++;
    }
    return res;
}
```

Changed — both existing calls must now pass `SubType` and `Faction`:

```csharp
// in JoinChannel():
character.SendPacket(new SCJoinedChatChannelPacket(ChatType, SubType, Faction, AnnouncedName));

// in SendMessage(origin, msg, ...):
m.SendPacket(new SCChatMessagePacket(ChatType, origin ?? m, msg, ability, languageType, SubType, Faction));
```

### 6.4 `ChatManager.cs` + `IChatManager.cs` — the global channel

```csharp
/// <summary>
/// The single server-wide channel (client calls it CSM, command /u).
/// </summary>
/// <remarks>
/// Unlike every other channel this one is not scoped: no faction, no zone, no group. Both
/// factions share it and on live it even spans servers, so it carries neither a SubType nor a
/// Faction - the client identifies it by ChatType.Csm alone.
/// </remarks>
private ChatChannel GlobalChannel { get; } = new()
{
    ChatType = ChatType.Csm, SubType = 0, Faction = 0, InternalId = 0, InternalName = "CSM"
};

public ChatChannel GetGlobalChat() => GlobalChannel;
```

Plus: `res.Add(GlobalChannel);` in `ListAllChannels()`, `GlobalChannel.LeaveChannel(character);` in
`LeaveAllChannels()`, and `ChatChannel GetGlobalChat();` on `IChatManager`.

### 6.5 `CSNotifyInGamePacket.cs` — announce after the fact

```csharp
var zoneChat = ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId);
if (!zoneChat.JoinChannel(Connection.ActiveChar))   // shout, trade, lfg
    zoneChat.AnnounceTo(Connection.ActiveChar);     // already a member from OnZoneChange - tell the client anyway

// ... existing nation / judge / faction joins ...

ChatManager.Instance.GetGlobalChat().JoinChannel(Connection.ActiveChar); // CSM - server-wide, both factions
```

### 6.6 `CSSendChatMessagePacket.cs` — routing

```csharp
case ChatType.RaidLeader:
case ChatType.Raid:
    // was: .SendPacket(new SCChatMessagePacket(type, ...))
    ChatManager.Instance.GetRaidChat(teamRaid).SendMessage(Connection.ActiveChar, type, message, ability, languageType);

case ChatType.Trade:
case ChatType.GroupFind:
case ChatType.Shout:
    ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId)
        .SendMessage(Connection.ActiveChar, type, message, ability, languageType);

case ChatType.Csm: // one server-wide channel, not scoped to faction or zone
    ChatManager.Instance.GetGlobalChat().SendMessage(Connection.ActiveChar, message, ability, languageType);
```

### 6.7 `SCChatMessagePacket.cs` — **Part B, 10.0.2.13 only**

Extend the constructor with two optional parameters, delete `WriteChatHeader()` outright, and
replace `Write()` with the measured layout. Full code in the appendix patch; the core:

```csharp
var chatType = (short)_type;
var faction  = (uint)_faction;
var chat = (ulong)(ushort)chatType
           | ((ulong)(ushort)_subType << 16)
           | ((ulong)faction << 32);

stream.Write((byte)0);                    // cliLocale - server-side messages carry no client locale
stream.Write(chat);                       // chat
stream.WriteBc(_character?.ObjId ?? 0);   // bc
stream.Write((long)chatType);             // type
stream.Write(_character != null ? _languageType : (byte)0);
stream.Write(_character != null ? (byte)_character.Race : (byte)0);
stream.Write(faction);
```

The old `System` special case in `WriteChatHeader()` (the `FF FE FF 00` sequence for the system
MOTD) goes away with it — it was an artefact of the wrong split.

---

## 7. Test plan

Verify with a real client after integrating:

| Channel | Command | Expectation |
|---------|---------|-------------|
| Say | `/s text` | visible nearby |
| Whisper | `/w Name text` | at sender and recipient |
| Shout | `/y text` | across the zone |
| Trade | `/trade text` | across the zone |
| LFG | `/lfg text` | across the zone |
| Nation | `/nation text` | everyone of the same race |
| Faction | `/faction text` | everyone of the same faction |
| CSM | `/u text` | server-wide, **including the opposing faction** |

Two things that used to fail and therefore deserve an explicit check:

- **Zone change**: after travelling to another zone, shout must still arrive there.
- **No more `Unsupported chat type` in the log** — that was the tell-tale symptom of cause 4.

The client's channel list should now also contain **1 (Shout)**, not just 6, 11 and 14.

---

## 8. Open points / known limitations

- **Untested:** Party, Raid, Team, Guild, Family, Trial and Commander. The systems behind them do
  not exist yet in the source project. They use this same channel machinery and should follow once
  those land, but it is not verified.
- **Judge/Trial** is still sent as a bare `SCJoinedChatChannelPacket` in `CSNotifyInGame` (no real
  channel), because there is no crime system yet.
- **Stale comment:** the `<remarks>` on `ChatManager.GlobalChannel` still says "the client
  identifies it by `ChatType.User` alone" — since commit #3 that is `ChatType.Csm`. Purely a
  documentation error with no effect on behaviour; fix it while porting. (It is already corrected in
  section 6.4 above.)
- **`cliLocale` is hard-coded to 0 when sending.** That is correct for server-generated messages; if
  a localised server message is ever needed, this is the place.

---

## 9. Appendix: full patch

Combined diff across all three commits (`aac433e4..7fe22ae5`). Apply with:

```
git apply chat-fix.patch
# or more tolerantly, if the target code has drifted:
git apply --3way --reject chat-fix.patch
```

On a vanilla target project the hunk for `SCChatMessagePacket.cs` will most likely **not apply
cleanly** — port section 6.7 by hand there, and read section 2 on client versions first.

```diff
diff --git a/AAEmu.Game/Core/Managers/ChatManager.cs b/AAEmu.Game/Core/Managers/ChatManager.cs
index 29ce09f1..c8d581ef 100644
--- a/AAEmu.Game/Core/Managers/ChatManager.cs
+++ b/AAEmu.Game/Core/Managers/ChatManager.cs
@@ -27,6 +27,19 @@ public class ChatManager : Singleton<ChatManager>, IChatManager
     private ConcurrentDictionary<FactionsEnum, ChatChannel> GuildChannels { get; }= new();
     private ConcurrentDictionary<long, ChatChannel> FamilyChannels { get; } = new();
 
+    /// <summary>
+    /// The single server-wide channel (client calls it CSM, command /u).
+    /// </summary>
+    /// <remarks>
+    /// Unlike every other channel this one is not scoped: no faction, no zone, no group. Both
+    /// factions share it and on live it even spans servers, so it carries neither a SubType nor a
+    /// Faction - the client identifies it by ChatType.User alone.
+    /// </remarks>
+    private ChatChannel GlobalChannel { get; } = new()
+    {
+        ChatType = ChatType.Csm, SubType = 0, Faction = 0, InternalId = 0, InternalName = "CSM"
+    };
+
     /// <summary>
     /// Creates default channels
     /// </summary>
@@ -44,9 +57,17 @@ public class ChatManager : Singleton<ChatManager>, IChatManager
         _ = AddNationChannel(Race.Nuian, FactionsEnum.NuiaAlliance, "Nuian-Elf-Dwarf");
         _ = AddNationChannel(Race.Hariharan, FactionsEnum.HaranyaAlliance, "Harani-Firran-Warborn");
 
+        // The global channel exists for the whole server lifetime; everyone joins it at login.
+        Logger.Info("Global chat channel '{0}' ready", GlobalChannel.InternalName);
+
         // Zone, Party/Raid, Guild, Family channels are created on the fly
     }
 
+    /// <summary>
+    /// The server-wide channel every character belongs to.
+    /// </summary>
+    public ChatChannel GetGlobalChat() => GlobalChannel;
+
     /// <summary>
     /// Used in GM command /testchatchannel list
     /// </summary>
@@ -64,6 +85,7 @@ public class ChatManager : Singleton<ChatManager>, IChatManager
         res.AddRange(RaidChannels.Values);
         res.AddRange(GuildChannels.Values);
         res.AddRange(FamilyChannels.Values);
+        res.Add(GlobalChannel);
         return res;
     }
 
@@ -87,6 +109,7 @@ public class ChatManager : Singleton<ChatManager>, IChatManager
             c.Value?.LeaveChannel(character);
         foreach (var c in FamilyChannels)
             c.Value?.LeaveChannel(character);
+        GlobalChannel.LeaveChannel(character);
     }
 
     /// <summary>
diff --git a/AAEmu.Game/Core/Managers/IChatManager.cs b/AAEmu.Game/Core/Managers/IChatManager.cs
index afcf7188..3b786b47 100644
--- a/AAEmu.Game/Core/Managers/IChatManager.cs
+++ b/AAEmu.Game/Core/Managers/IChatManager.cs
@@ -1,4 +1,4 @@
-using AAEmu.Game.Models.Game.Char;
+﻿using AAEmu.Game.Models.Game.Char;
 using AAEmu.Game.Models.Game.Chat;
 using AAEmu.Game.Models.Game.Expeditions;
 using AAEmu.Game.Models.Game.Team;
@@ -16,6 +16,7 @@ public interface IChatManager : IInitializable
     ChatChannel GetNationChat(Race race);
     ChatChannel GetNationChat(Character character);
     ChatChannel GetZoneChat(uint zoneKey);
+    ChatChannel GetGlobalChat();
     ChatChannel GetGuildChat(Expedition guild);
     ChatChannel GetFamilyChat(uint familyId);
     ChatChannel GetPartyChat(Team party, Character myChar);
diff --git a/AAEmu.Game/Core/Packets/C2G/CSNotifyInGamePacket.cs b/AAEmu.Game/Core/Packets/C2G/CSNotifyInGamePacket.cs
index 065baef0..28d9315a 100644
--- a/AAEmu.Game/Core/Packets/C2G/CSNotifyInGamePacket.cs
+++ b/AAEmu.Game/Core/Packets/C2G/CSNotifyInGamePacket.cs
@@ -1,4 +1,4 @@
-using AAEmu.Commons.Network;
+﻿using AAEmu.Commons.Network;
 using AAEmu.Game.Core.Managers;
 using AAEmu.Game.Core.Managers.World;
 using AAEmu.Game.Core.Network.Game;
@@ -76,11 +76,14 @@ public class CSNotifyInGamePacket() : GamePacket(CSOffsets.CSNotifyInGamePacket,
 
         // Joining channel 1 (shout) will automatically also join /lfg and /trade for that zone on the client-side
         // Back in 1.x /trade was zone based, not faction based
-        ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId).JoinChannel(Connection.ActiveChar); // shout, trade, lfg
+        var zoneChat = ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId);
+        if (!zoneChat.JoinChannel(Connection.ActiveChar)) // shout, trade, lfg
+            zoneChat.AnnounceTo(Connection.ActiveChar);  // already a member from OnZoneChange - tell the client anyway
         ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).JoinChannel(Connection.ActiveChar); // nation
         // TODO: Implement crime system, actual jury channel doesn't exist yet
         Connection.ActiveChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Judge, 0, Connection.ActiveChar.Faction.MotherId)); //trial
         ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId).JoinChannel(Connection.ActiveChar); // faction
+        ChatManager.Instance.GetGlobalChat().JoinChannel(Connection.ActiveChar); // CSM - server-wide, both factions
 
         // TODO: Maybe move to spawn character?
         TeamManager.Instance.UpdateAtLogin(Connection.ActiveChar);
diff --git a/AAEmu.Game/Core/Packets/C2G/CSSendChatMessagePacket.cs b/AAEmu.Game/Core/Packets/C2G/CSSendChatMessagePacket.cs
index b35671cc..71a058dc 100644
--- a/AAEmu.Game/Core/Packets/C2G/CSSendChatMessagePacket.cs
+++ b/AAEmu.Game/Core/Packets/C2G/CSSendChatMessagePacket.cs
@@ -88,7 +88,7 @@ public class CSSendChatMessagePacket() : GamePacket(CSOffsets.CSSendChatMessageP
                     }
                     else
                     {
-                        ChatManager.Instance.GetRaidChat(teamRaid).SendPacket(new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType));
+                        ChatManager.Instance.GetRaidChat(teamRaid).SendMessage(Connection.ActiveChar, type, message, ability, languageType);
                     }
                 }
                 else
@@ -111,9 +111,8 @@ public class CSSendChatMessagePacket() : GamePacket(CSOffsets.CSSendChatMessageP
             case ChatType.GroupFind: //lfg
             case ChatType.Shout: //shout
                 // We use SendPacket here so we can fake our way through the different channel types
-                ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId).SendPacket(
-                    new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType)
-                    );
+                ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId)
+                    .SendMessage(Connection.ActiveChar, type, message, ability, languageType);
                 break;
             case ChatType.Clan:
                 if (Connection.ActiveChar.Expedition != null)
@@ -148,6 +147,9 @@ public class CSSendChatMessagePacket() : GamePacket(CSOffsets.CSSendChatMessageP
             case ChatType.Region: //nation (birth place/race, includes pirates etc)
                 ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).SendMessage(Connection.ActiveChar, message, ability, languageType);
                 break;
+            case ChatType.Csm: // one server-wide channel, not scoped to faction or zone
+                ChatManager.Instance.GetGlobalChat().SendMessage(Connection.ActiveChar, message, ability, languageType);
+                break;
             case ChatType.Ally: //faction (by current allegiance)
                 ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId).SendMessage(Connection.ActiveChar, message, ability, languageType);
                 break;
diff --git a/AAEmu.Game/Core/Packets/G2C/SCChatMessagePacket.cs b/AAEmu.Game/Core/Packets/G2C/SCChatMessagePacket.cs
index de15308e..928834a6 100644
--- a/AAEmu.Game/Core/Packets/G2C/SCChatMessagePacket.cs
+++ b/AAEmu.Game/Core/Packets/G2C/SCChatMessagePacket.cs
@@ -2,6 +2,7 @@
 using AAEmu.Game.Core.Network.Game;
 using AAEmu.Game.Models.Game.Char;
 using AAEmu.Game.Models.Game.Chat;
+using AAEmu.Game.Models.StaticValues;
 
 namespace AAEmu.Game.Core.Packets.G2C;
 
@@ -15,13 +16,18 @@ public class SCChatMessagePacket : GamePacket
     private readonly int _ability;
     private readonly byte _languageType;
 
+    /// <summary>Channel discriminators, mirroring what SCJoinedChatChannel announced for this channel.</summary>
+    private readonly short _subType;
+    private readonly FactionsEnum _faction;
+
     public SCChatMessagePacket(ChatType type, string message) : base(SCOffsets.SCChatMessagePacket, 1)
     {
         _type = type;
         _message = message;
     }
 
-    public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType) :
+    public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType,
+        short subType = 0, FactionsEnum? faction = null) :
         base(SCOffsets.SCChatMessagePacket, 1)
     {
         _type = type;
@@ -29,25 +35,60 @@ public class SCChatMessagePacket : GamePacket
         _message = message;
         _ability = ability;
         _languageType = languageType;
+        _subType = subType;
+        // Deliberately NOT the character's faction. Only a real ChatChannel knows the discriminators
+        // it announced in SCJoinedChatChannel; a direct broadcast (say, whisper) has none, and filling
+        // them in from the sender names a channel the client never joined - which silently swallowed
+        // those messages until this defaulted back to zero.
+        _faction = faction ?? 0;
     }
 
     public override PacketStream Write(PacketStream stream)
     {
-        // Wire layout validated against CN 10.0.2.13 live sniff (SCChatMessage 0x102):
-        //   26-byte header + name + msg + 4×linkType(u8) + ability(i32) + 3-byte trailer.
-        // Truncating this body caused "not enough buffer for option/worldId" → sc desync → DC.
-        WriteChatHeader(stream);
-        stream.WriteBc(_character?.ObjId ?? 0);
-        stream.Write(_character?.Id ?? 0);
+        // Header field order read out of the client's own serializer, which names every value
+        // (SCChatMessage::Read, VA 0x39C71C30):
+        //
+        //   cliLocale     u8    1     off  0
+        //   chat          u64   8     off  1
+        //   bc            raw   3     off  9
+        //   type          i64   8     off 12
+        //   LanguageType  u8    1     off 20
+        //   CharRace      u8    1     off 21
+        //   type          u32   4     off 22
+        //                      ---
+        //                       26
+        //
+        // Widths come from the archive's vtable thunks: +0x90 reads 1, +0x88 reads 2, +0x80 and
+        // +0xA0 read 4, +0x98 reads 8, +0x1A0 reads the 3-byte compressed id, and +0x1A8/+0x1D0
+        // consume nothing at all (they are `ret` stubs on the read side).
+        //
+        // The previous layout was reconstructed from a byte sniff and got the SIZE right - also 26 -
+        // but split it as i16+i16+u32 | bc | u32 | u8 | u8 | u32+u32+u8. Everything therefore sat one
+        // to five bytes off: bc at 8 instead of 9, LanguageType at 15 instead of 20, CharRace at 16
+        // instead of 21. name and msg still began at 26, which is why nothing desynced and no message
+        // was ever truncated - but the client read `chat` and `type` out of the wrong bytes, could not
+        // resolve either to a real channel, and dropped every message it was handed.
+        //
+        // CSSendChatMessage already carries this correction on the receiving side, including the
+        // detail that ChatType lives in the low 16 bits of the 8-byte `chat` field; this mirrors it.
+        // `chat` is the same 8-byte channel descriptor SCJoinedChatChannel announces:
+        // type (i16) | subType (i16) | factionId (u32). CSSendChatMessage reads it back the same way,
+        // taking the ChatType out of the low 16 bits. Sending only the type - subType and faction left
+        // at zero - names a channel the client never joined, so it drops the message for every channel
+        // that is scoped by zone, party or faction. Say and whisper survive it because theirs are zero.
+        var chatType = (short)_type;
+        var faction = (uint)_faction;
+        var chat = (ulong)(ushort)chatType
+                   | ((ulong)(ushort)_subType << 16)
+                   | ((ulong)faction << 32);
+
+        stream.Write((byte)0);                    // cliLocale - server-side messages carry no client locale
+        stream.Write(chat);                       // chat
+        stream.WriteBc(_character?.ObjId ?? 0);   // bc
+        stream.Write((long)chatType);             // type
         stream.Write(_character != null ? _languageType : (byte)0);
         stream.Write(_character != null ? (byte)_character.Race : (byte)0);
-
-        // 9 bytes after race (sniff): u32 + u32 + u8. System MOTD is all zero; player chat
-        // carries faction-like values in this block.
-        var faction = (uint)(_character?.Faction.Id ?? 0);
         stream.Write(faction);
-        stream.Write(faction);
-        stream.Write((byte)0);
 
         if (_character?.Connection?.GetAttribute("gmFlag") != null)
             stream.Write("GM " + _character.Name);
@@ -79,21 +120,4 @@ public class SCChatMessagePacket : GamePacket
         return stream;
     }
 
-    private void WriteChatHeader(PacketStream stream)
-    {
-        // System MOTD sniff starts FF FE FF 00 (not FE FF from (short)ChatType.System=-2).
-        if (_type == ChatType.System && _character == null)
-        {
-            stream.Write((byte)0xFF);
-            stream.Write((byte)0xFE);
-            stream.Write((byte)0xFF);
-            stream.Write((byte)0x00);
-            stream.Write(0u); // pad to 8-byte chat block
-            return;
-        }
-
-        stream.Write((short)_type);
-        stream.Write((short)(_character?.Faction.Id ?? 0));
-        stream.Write((uint)(_character?.Faction.Id ?? 0));
-    }
 }
diff --git a/AAEmu.Game/Core/Packets/G2C/SCJoinedChatChannelPacket.cs b/AAEmu.Game/Core/Packets/G2C/SCJoinedChatChannelPacket.cs
index 15b3fd13..2e5c06c9 100644
--- a/AAEmu.Game/Core/Packets/G2C/SCJoinedChatChannelPacket.cs
+++ b/AAEmu.Game/Core/Packets/G2C/SCJoinedChatChannelPacket.cs
@@ -5,7 +5,7 @@ using AAEmu.Game.Models.StaticValues;
 
 namespace AAEmu.Game.Core.Packets.G2C;
 
-public class SCJoinedChatChannelPacket(ChatType type, short subType, FactionsEnum factionId)
+public class SCJoinedChatChannelPacket(ChatType type, short subType, FactionsEnum factionId, string name = "")
     : GamePacket(SCOffsets.SCJoinedChatChannelPacket, 1)
 {
     public override PacketStream Write(PacketStream stream)
@@ -14,7 +14,8 @@ public class SCJoinedChatChannelPacket(ChatType type, short subType, FactionsEnu
         stream.Write(subType);
         stream.Write((uint)factionId);
         // -------------
-        stream.Write(""); // name
+        // User channels are addressed by name in the UI; the fixed ones carry none.
+        stream.Write(name ?? "");
         return stream;
     }
 }
diff --git a/AAEmu.Game/Models/Game/Chat/ChatChannel.cs b/AAEmu.Game/Models/Game/Chat/ChatChannel.cs
index 2186490e..1a389d7b 100644
--- a/AAEmu.Game/Models/Game/Chat/ChatChannel.cs
+++ b/AAEmu.Game/Models/Game/Chat/ChatChannel.cs
@@ -52,11 +52,29 @@ public class ChatChannel
 
         // character.SendMessage(ChatType.System, "ChatManager.JoinChannel {0} - {1} - {2}", chatType, internalId, internalName);
         Members.Add(character);
-        character.SendPacket(new SCJoinedChatChannelPacket(ChatType, SubType, Faction));
+        character.SendPacket(new SCJoinedChatChannelPacket(ChatType, SubType, Faction, AnnouncedName));
 
         return true;
     }
 
+    /// <summary>Only user channels are addressed by name; the fixed ones are identified by type alone.</summary>
+    private string AnnouncedName => ChatType is ChatType.User or ChatType.Csm ? InternalName : string.Empty;
+
+    /// <summary>
+    /// Re-announces this channel to a character that is already a member.
+    /// </summary>
+    /// <remarks>
+    /// Membership and the client's knowledge of a channel are not the same thing. The zone channel in
+    /// particular is joined from Character.OnZoneChange while the login position is applied, long
+    /// before the client can take a chat packet - so by the time CSNotifyInGame runs, JoinChannel sees
+    /// an existing member and stays silent, and the client never learns the channel exists. Shout,
+    /// Trade and LFG all ride that one channel, which is exactly the set that stayed dead.
+    /// </remarks>
+    public void AnnounceTo(Character character)
+    {
+        character?.SendPacket(new SCJoinedChatChannelPacket(ChatType, SubType, Faction, AnnouncedName));
+    }
+
     /// <summary>
     /// Removes a character from the channel
     /// </summary>
@@ -84,12 +102,32 @@ public class ChatChannel
     /// <param name="ability"></param>
     /// <param name="languageType"></param>
     /// <returns>Number of members the message was sent to</returns>
+    /// <summary>
+    /// Sends a message whose ChatType differs from the channel's own.
+    /// </summary>
+    /// <remarks>
+    /// Trade, LFG and Shout all ride the zone channel, and Raid/RaidLeader share the raid one, so the
+    /// message type and the channel type are not the same thing. The channel's SubType and Faction
+    /// still have to travel with it - they are what lets the client match the message to a channel it
+    /// joined - which is why these callers cannot simply build the packet themselves.
+    /// </remarks>
+    public int SendMessage(Character origin, ChatType type, string msg, int ability = 0, byte languageType = 0)
+    {
+        var res = 0;
+        foreach (var m in Members)
+        {
+            m.SendPacket(new SCChatMessagePacket(type, origin ?? m, msg, ability, languageType, SubType, Faction));
+            res++;
+        }
+        return res;
+    }
+
     public int SendMessage(Character origin, string msg, int ability = 0, byte languageType = 0)
     {
         var res = 0;
         foreach (var m in Members)
         {
-            m.SendPacket(new SCChatMessagePacket(ChatType, origin ?? m, msg, ability, languageType));
+            m.SendPacket(new SCChatMessagePacket(ChatType, origin ?? m, msg, ability, languageType, SubType, Faction));
             res++;
         }
         return res;
diff --git a/AAEmu.Game/Models/Game/Chat/ChatType.cs b/AAEmu.Game/Models/Game/Chat/ChatType.cs
index d23d41a0..710c7803 100644
--- a/AAEmu.Game/Models/Game/Chat/ChatType.cs
+++ b/AAEmu.Game/Models/Game/Chat/ChatType.cs
@@ -1,4 +1,4 @@
-namespace AAEmu.Game.Models.Game.Chat;
+﻿namespace AAEmu.Game.Models.Game.Chat;
 
 public enum ChatType : short
 {
@@ -19,5 +19,12 @@ public enum ChatType : short
     RaidLeader = 10,
     Judge = 11,
     Ally = 14,
-    User = 15
+    User = 15,
+
+    /// <summary>
+    /// The server-wide channel the client labels CSM (command /u). Measured from a live client:
+    /// it sends type 18 with an otherwise empty channel descriptor (chat = 0x12, no subType, no
+    /// faction), which fits a channel scoped to nothing at all.
+    /// </summary>
+    Csm = 18
 }
\ No newline at end of file
```
