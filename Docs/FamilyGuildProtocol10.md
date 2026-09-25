# Family and Guild Protocol Evidence (10.x)

The field lists below describe the packet bodies of the 10.0.2.13 client. Every packet carries the
common packet header first; that header is written and read by shared code, so its fields are not
repeated in the per-packet lists. Each server-to-client body is written in the order shown, and each
client-to-server body is the payload the client sends.

## Family server policy

The client proves the guide values (a 604800-second role-change period and the configured family
departure percentage) and transports `roleUpdateTime`, but it does not contain the authoritative
World-server algorithms. AAEmu permits a first role assignment when the timestamp is zero, then
uses Unix seconds and a 604800-second cooldown for explicit role changes. Voluntary leave and owner
kick apply `family_leave_dec_exp`, truncate the lost amount, and retain the explicitly purchased
family level. These are documented server policy choices, not observations about the original
server.

The same policy reads family item IDs, rename delay, base member count, maximum level, login EXP,
departure EXP, and rejoin delay from kind-33 `content_configs`. Login EXP is granted once per member
per UTC day and persisted on the family-member row. Departure stores a character rejoin deadline in
Unix seconds; invitations are rejected until the configured hour count has elapsed.

The exact keys are `family_join_leave_item`, `family_leave_dec_exp`, `family_login_inc_exp`,
`family_max_count`, `family_max_level`, `family_name_change_delay`, `family_name_change_item`,
`family_name_change_item_count`, and `family_rejoin_delay_time`. Expansion costs and successive caps
come from `family_member_limits`; family levels, buffs, and roles come from their respective family
content tables. Missing required rows fail loading instead of silently substituting constants.

## Family wire layouts

`SCFamilyCreatedPacket` and `SCFamilyDescPacket` write the shared `FamilyDesc` structure.
`SCFamilyMemberAddedPacket` writes the same descriptor followed by a `u32 addedIndex`.

`FamilyDesc` is:

1. `i32 familyId`, `u32 memberCount`.
2. Each member: `u64 characterId`, string `name`, `i8 level`, `i8 heirLevel`, `i8 role`, `bool online`,
   string `title`, `u64 roleUpdateTime`.
3. String `familyName`, `u32 level`, `u32 exp`, string `notice`, `u32 incMemberCount`, `i64 resetTime`,
   `i64 changeNameTime`.
4. `i32 actSanctionCount`, then `{u8 key, i64 value}` for each sanction.

The descriptor member storage limit is 60. The current game API supplies a separate family
domain limit of 12. Read limits are 128 bytes for member names, 256 for family names, and 800
for notices. The title read limit could not be established reliably; the invitation title buffer is 104
bytes.

Other proven server-to-client bodies are:

- `SCFamilyInfoSetPacket`: `i32 familyId`, `u32 level`, `u32 exp`, string `name`, string
  `notice`, `i32 type`, `u32 incMemberCount`, `i64 changeNameTime`.
- `SCFamilyMemberRemovedPacket`: `i32 familyId`, `u64 memberId`, `bool kicked`.
- `SCFamilyOwnerChangedPacket`: `i32 familyId`, `u64 oldOwnerId`, `u64 newOwnerId`.
- `SCFamilyTitleChangedPacket`: `i32 familyId`, `u64 memberId`, string `title`.
- `SCFamilyMemberNameChangedPacket`: `i32 familyId`, `u64 memberId`, string `newName`.
- `SCFamilyMemberOnlinePacket`: `i32 familyId`, `u64 memberId`, `bool online`, `i8 level`,
  `i8 heirLevel`.
- `SCFamilyChangeMemberRolePacket`: `i32 familyId`, `u64 memberId`, `i32 role`.
- `SCFamilyChangeMemberLevelPacket`: `i32 familyId`, `u64 memberId`, `i8 level`, `i8
  heirLevel`.
- `SCFamilyExpChangeNotifyPacket`: `i32 familyId`, `u64 source`, `u32 level`, `u32 exp`.
- `SCFamilyExpChangeDryNotifyPacket`: `u32 expDiff`.

Proven client-to-server bodies are: ChangeMemberRole `u64 characterId, i32 role`,
ChangeOwner `u64 characterId`, ChangeTitle `u64 characterId, string title`,
Kick `u64 characterId`, InviteMember `string name, string title`,
NameSet `string name`, NoticeSet `string notice`,
and ReplyInvitation `u64 invitorId, bool join, string role`.
IncreaseMember and Leave have empty bodies.

`SCFamilyInvitationPacket` labels its third field `family` and writes it as an `i32`. AAEmu
therefore sends the inviter's current family ID, or zero while inviting the second
member who will create a new family on acceptance. That value choice is the server interpretation
of the field name; client Lua does not consume the field. The earlier literal `1` had no
support in the current client.

The current Lua chat event path recognizes only `CHAT_EXPEDITION`; no separate manager/officer chat
channel constant or caller was found. The role-policy structure still transports `managerChat`, but
AAEmu does not invent a wire chat type for it. Standard guild chat checks the member's authoritative
roster entry and that role's `chat` permission. Family chat similarly rechecks authoritative
membership while serialized with family leave/kick operations.

The client `IsFamily` path tests whether the local character's family ID is nonzero. `GetInfo` also
requires a cached descriptor containing the local member. An `IsFamily == true` and `GetInfo == nil`
state therefore indicates missing or rejected descriptor/member state.

## Implemented family behavior and persistence

Family creation and joining consume one `family_join_leave_item` from the inviter when an invitation
is successfully issued; declining or accepting consumes no second item. A voluntary leave consumes
one from the leaving member and a kick consumes one from the owner; the roster change and the item
consumption commit in one transaction, and without the item the request is ignored. The serialized
character-deletion cleanup removes the character without a charge. Member expansion uses
`family_member_limits`, and rename uses the configured item, count, and delay. Inventory consumption
and the family mutation share one MySQL transaction. Packets, item-use callbacks, chat changes, and
buff changes run after commit and revalidate the current roster before stateful publication.

Client changes require the exact live character object in the World registry and its connection's
`ActiveChar`. Login/logout, quest EXP, and serialized character-deletion cleanup use trusted server
paths. Owner deletion is rejected while the authoritative roster still identifies that character as
owner.

The aggregate is stored in `families`, `family_members`, and `family_act_sanctions`;
`characters.family` remains the association. Loading accepts a member row only when that association
matches, preventing stale legacy rows from enrolling one character twice. The migration widens
titles, normalizes legacy NULL titles, and adds role/login timestamps and rejoin deadlines. Paid
rename and expansion use optimistic updates, so stale item or aggregate snapshots roll back both.

## Guild wire layouts

`SCExpeditionDescPacket` writes:

`FactionDesc`, `i32 level`, `u32 exp`, `i64 protectDate`, `u32 warDeposit`, `u32 dailyExp`, `u64
lastExpUpdateTime`, `i16 interest`, string `notice`, `u32 win`, `u32 lose`, `u32 draw`, `i32 unnamed`,
`u64 unnamed`, `u64 contributionPoint`, `u32 dailyContributionPoint`, `u64
lastContributionPointAdded`, `u64 lastAssignmentUpdateTime`.

`GetMyExpeditionContributionPoint` reads the descriptor's contribution-point field (`+0x4b0`). The
contribution-currency shop uses that getter, while AAEmu debits the corresponding
`ExpeditionMember.ContributionPoint`, so AAEmu serializes a separate descriptor for each recipient
with that member's balance. The level-up UI role gate instead reads the local
`ExpeditionMemberDesc.Role`; it does not establish a meaning for either unnamed descriptor field.

`SCExpeditionListPacket` and `SCExpeditionRolePolicyListPacket` use a `u8 count` with a fixed
maximum of 20. A role policy is `u32 expeditionId`,
`u8 role`, string `name`, followed by ten booleans: dominionDeclare, invite, expel, promote, dismiss,
chat, managerChat, siegeMaster, joinSiege, and useInstance. `SCExpeditionRolePolicyChangedPacket`
writes one policy and a `bool success`.

The player's current expedition/guild faction ID is the value that descriptor, role,
history-receiver, and debug paths read; it is not a sponsor faction ID. The player's
top faction is tracked separately.

Guild creation sponsor selection is separate. `GetSponsorFaction` filters system
factions by flag `0x10` (`show_create_expedition`) and requires the candidate's mother to equal the
player's top faction. The create callback forwards the selection. AAEmu stores that
sponsor child in `Expedition.MotherId`; alliance checks resolve its mother/root. Current content
examples include faction 101 with mother 148 and faction 108 with mother 149; eligibility comes from
the relationship and flag rather than a hardcoded ID range.

## Implemented guild behavior and persistence

Guild persistence covers level/EXP, role policies including `useInstance`, descriptor state,
recruitment listings and applications, rename history, contribution periods, activities, retained war
history, and deletion associations. The 2026-09-13 migrations are
`aaemu_game_expedition_activities`, `daily_exp`, `descriptor_state`, `instance_histories`,
`instance_history_type`, `recruitment`, `renames`, `weekly_contribution`, `public_assignments`, and
`families`; the baseline schema contains the same structures. Installations
from older revisions must also apply the earlier expedition level, contribution, role-instance, buff,
interest, residence, dominion, and war migrations in normal order.

Recruitment and membership transitions lock and validate authoritative character/guild rows. Stale
connection objects cannot mutate a replacement session's application state. Standard guild chat
checks current membership and the role's `chat` permission. The client exposes no proven officer chat
channel, so `managerChat` is stored and transported without inventing a chat type. Activity and quest
rewards enter normal Expedition EXP processing. Daily and weekly rollovers use AAEmu's shared UTC-day
and Monday-week policy; this is not proof of the original reset boundaries.

The sort-6 public assignment board is guild-owned weekly state. AAEmu selects one content-defined
quest for each eligible public step, persists its objective counters and selection identity, and
queues immutable progress events after the originating character action. The single consumer uses
the normal persistence-then-guild lock order. A reroll or weekly replacement invalidates queued
events from the old selection; ordinary optimistic progress-version changes do not discard a burst
of events for the same selection. Legacy per-character sort-6 rows and active quests are removed on
load so they cannot duplicate a shared reward. The selection period starts on the content-configured
`expedition_public_quest_reset_weekly_day` (0 Sunday through 6 Saturday); the daily scheduler also
checks this boundary so online guilds roll over without waiting for a UI request.

At completion, guild EXP is credited once. Members who are on the current roster and contributed
positive matching progress receive the content-defined personal contribution points and item bundle;
items are delivered through durable mail. The entitlement is staged in the same transaction as completion,
survives later logout or departure, and excludes members who join after completion. The owner alone
may reroll, and the request amount must be zero. The current client initializes its corresponding
value to zero and no setter or configuration receiver is known; free rerolls are an explicit AAEmu
policy, not a retail-server price claim.

## Guild activity and instance evidence

The instance-history list callback and new-history callback both read the player's current
expedition ID through the same faction getter; the outer ID is therefore the current
expedition ID. The list body is `bool isEnd`, `u8 count` (cap 20), `u32 expeditionId`, then
records. The new-history body is `u32 expeditionId`, one rating summary, then one record. A rating
summary is six `u32` fields: `type`, `win`, `loss`, `draw`, `rating`, and `bestRating`.

Each record contains `u64 historyId`, an `i32` rating-detail/type field, `i32 instanceId`, `u32
score`, `u8 result`, `i64 time`, and a member list. Members contain `u64 historyId`, `u64 characterId`,
and `u8 status`. Observed client meanings are status 0 started, status 1 finished, and result 1 loss,
2 draw, 3 win. The client resolves the second `i32` through `instances` for its display name,
proving that field is `instances.id`. The preceding type is derived from
`instance_rank_details.id`: the client loads that table's `id`, `instance_id`, and `rating_only`,
and current content relates row 41 to instance 69. This mapping is a content-backed relational
inference; no direct lookup or rating algorithm is known.

The `IsExpeditionContents` check requires instance field `+0xb8 == 0`, a nested record, and
nested UI kind 5. Content defines `instance_ui_kinds.id=5` as Expedition; instance 69 targets
BattleField 30 with UI kind 5 and `squad_not_use=false`. Mapping `+0xb8` specifically to
`squad_not_use` is an evidence-backed inference, not symbolic proof. The 60-second pending
summon expiry is AAEmu server policy aligned with the shipped dialog display time; it is not a proven
retail-server deadline. Portal/summon yaw follows AAEmu's radians convention, while the wire
unit has not been separately proven.

## Family administration slice (S03)

The merged family request packets are now routed through the existing owner-authority and persistence
paths for the four administration actions covered by S03:

- `CSFamilyChangeMemberRolePacket` (`u64 memberId`, `u32 roleId`) checks that the caller is the current
  family owner, the target is a roster member, the role exists in `family_roles`, the target is not the
  owner, and the role's content `role_count` has room. A successful change stores `role` and
  `role_update_time` through `family_members` and publishes `SCFamilyChangeMemberRolePacket`.
- `CSFamilyIncreaseMemberPacket` (no body) selects only the next consecutive `family_member_limits` row
  and passes that row's item and count to the family purchase service. The optimistic family-row update
  and item consumption share one transaction; a rejected or stale expansion does not mutate the live
  family. The expanded descriptor is published after the commit.
- `CSFamilyNameSetPacket` (`string name`) requires the current owner, the existing name policy, and the
  configured name-change delay. For an existing family name, the configured ticket/item count and the
  family row update commit together through the purchase service; the first name of a newly created
  family remains the no-cost path. The authoritative descriptor is published after commit.
- `CSFamilyNoticeSetPacket` (`string notice`) requires the current owner and enforces the 800-byte UTF-8
  read limit before persistence. The notice is written in the family transaction and the descriptor is
  published only after the commit.

Family-name validation keeps the existing 12-rune/letter policy and now also checks the 256-byte family-name
read limit. Role and capacity decisions are centralized in `FamilyProgressionRules` and consume the loaded
`family_roles` and `family_member_limits` catalogs; no role id, cap, item id, count, or limit is introduced
by this slice. `SCFamilyChangeMemberRolePacket` and descriptor wire tests cover the proven field order and
UTF-8 boundaries; the existing family purchase integration tests cover optimistic conflict and rollback.

**Blocked remainder:** the client exposes a 604800-second role-change guide period, but no shipped
content row or server algorithm establishes the authoritative cooldown. The existing value remains
policy in the merged code and is not treated as retail parity here. The optional `SCFamilyNameChangeNotifyPacket`
(0x062) has no server-side caller in the current family path; the safe slice uses the proven full
`SCFamilyDescPacket` broadcast. Neither gap is guessed or added in this slice.

## Deployment and verification

Apply ordered `SQL/updates` migrations while World is stopped, then start World so content data and
social aggregates load normally. Do not edit player rows while World is running. Development builds,
temporary databases, and probes stay outside the repository. Family and expedition integration tests
create isolated MySQL schemas and execute product migrations before save/load and rollback checks.

`GameNetworkSocialPacketRegistrationTests` checks that every family and expedition request class has a
`CSOffsets` constant and is registered with the protocol handler; an unregistered class is silently dropped
as an unknown opcode at runtime. When driving the 10.0.2.13 client through Lua, its word filter rejects
Latin free text in notice, recruitment, and role-name fields before any packet is sent, so use CJK text for
those checks.

Run `AAEmu.UnitTests`, the focused `AAEmu.IntegrationTests` family/expedition fixtures, and the protocol
harness against the matching client build before deployment. Record result counts in the change or
deployment report, where they identify the exact tested revision, rather than treating a prior
working-tree count as continuing evidence.

## Unresolved semantics

Serializer evidence establishes widths and order. It does not establish the meaning or required
values of the two unnamed expedition descriptor fields, the family sanction map, or the family EXP
notification source. The client exposes a 604800-second role-change period and a configured leave EXP
percentage, but it does not prove kick behavior, rounding, cooldown initialization, or whether a
purchased level survives EXP loss. AAEmu's choices are stated under Family server policy rather than
presented as retail-server behaviour. Once-per-UTC-day login EXP and the configured-hour rejoin
interpretation are also server policy pending further evidence.

No current content rows define `AddFamilyExp` operands, so that special-effect behavior remains
unavailable until evidence establishes it. Guild invitation reply polarity follows the shared dialog
result: the client forwards that result through the packet builder to the shared dialog handler,
which uses result 0 for affirmative. Wire `false` therefore accepts and wire `true` declines. The
client exposes no packet or configuration path for advertising a
nonzero public-assignment reroll price, so nonzero pricing remains disabled by the stated AAEmu
default policy. Nothing observed shows whether a daily contribution limit of zero means unlimited
or disabled. Sponsor selection is
proven above; downstream rules for every sponsor-dependent benefit still need behavioral evidence.
