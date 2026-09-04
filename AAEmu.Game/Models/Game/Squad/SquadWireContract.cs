namespace AAEmu.Game.Models.Game.Squad;



/// <summary>

/// Locked 10.0.2.13 squad wire contract (client ISerialize IR + DynamicPacket).

/// Do not "fix" one field at a time — change against this contract only.

///

/// Shared nested DynamicPacket. Two stages: the network thread copies the blob into the
/// packet, then the main thread parses the saved copy (that deferred parse is what throws
/// on a malformed blob). After the outer fields, with N = payload length:
///   u16 N + 4             transport size
///   u16 N + 4             repeated; the copier bounds its own read against the first
///   u16 N + 2             covers tag + payload; becomes the reader's limit (+2)
///   u16 tag = 0
///   bytes[N]              payload
///
/// The client's buffer owns a 4-byte prologue (the length word and the tag) and starts its
/// read cursor at offset 4, deriving its limit as buffer[0..1] + 2. Omitting the prologue
/// leaves limit below the start cursor, and the reader answers that with a throw the client
/// does not catch. Empty payload is legal and reproduces a default-constructed buffer.

///

/// --- CS (client → world) ---

/// CSRequestSquadList 0x1D7: FieldType(u8 kind=1,u32 catalogId,u64 data=0), i32 page

/// CSCreateSquad      0x1D8: FieldType, i32 openType, bool partyInvitation, string explanation,

///                            u8 limitLevel, i32 limitGearScore

/// CSDisbandSquad     0x1D9: (empty)

/// CSReadySquad       0x1DA: bool ready, FieldType

/// CSJoinSquadMember  0x1DB: i32 squadId, i32 type, i32 invitationId, i32 joinKey

/// CSLeaveSquadMember 0x1DD: (empty)

/// CSInviteSquadMember 0x1DE: string charName, u8 worldId

/// CSApplySquadMatching 0x1DF: (empty / TBD)

///

/// --- SC (world → client) ---

/// SCSelectSquadList  0x30B: u32 available, u32 curPage, DP payload = u32 count + count×SquadBase

/// SCCreateSquad      0x30C: u8/bool ignoreMinGameSize, DP payload = one SquadBase (mask 0x0F)

/// SCDisbandSquad     0x30D: (empty)

/// SCInviteSquadMember 0x30E: u32 squadId, u64 worldCharKey, string inviterCharName,

///                            FieldType, u32 invitationId

/// SCReadySquad       0x30F: u64 worldCharKey, bool ready, u16 errorMessage

/// SCConnectStateMember 0x310: u64 worldCharKey, bool offline

/// SCJoinSquadMember  0x311: u64 worldCharKey, string charName, u8 level, u8 ability×3, i32 eloRating

/// SCRefuseSquadInvitation 0x312: u64 worldCharKey, u8 refuseType

/// SCLeaveSquadMember 0x313: u64 worldCharKey, u8 mask, bool expelled, DP header-only empty

/// SCDelegateSquadLeader 0x315: u64 worldCharKey

/// SCNotifySquadEvent 0x316: u8 eventType, u64 recruitId

/// SCChangeSquadOpenType 0x317: u8 openType

/// SCSquadSetGameInfo 0x31A: u8 destination, bool gameStarted

/// SCChangeSquadMemberRating 0x31C: u64 worldCharKey, u32 rating

///

/// SquadBase (DP payload, SquadBase reader0, mask 0x0F). Lua listInfo fields are built client-side

/// in the client reader0; several are NOT on the wire (isMySquad, buttonEnable, buttonType).

/// ownerName / worldName resolve via the member keys and the leader lookup (+120).

///

/// Mask bit 1 (0x01) — fixed prefix:

///   u32 squadId

///   i32 headerField + u8 headerByte + 3 pad bytes        // object +28/+32

///   u32 fieldKind + u32 instanceId + u64 fieldValue      // object +40/+44/+48
///                                                        // instanceId is read back out of +44
///                                                        // before the client will start matching

///   u32 catalogWireId                                    // object +104

///   u64 leaderWorldCharKey                               // object +112

///   u16-len UTF-8 explanationText                        // object +64 std::string

///   u64 matchingKey + u8 isJoining + 7 pad               // object +160/+168
///                                                        // both must be set before the client
///                                                        // will confirm entering the instance

///   u8 isStarted + u8 gameWorld                          // object +176/+177
///                                                        // isStarted must be clear until the
///                                                        // squad is inside, or entry is refused

///   u32 publicKey                                        // object +180

///   u8 limitLevel + 3 pad + i32 limitGearScore           // object +184..+188 (8 bytes)

///

/// Mask bit 2 (0x02):

///   i32 openType                                         // object +56

///

/// Mask bit 8 (0x08):

///   u8 memberCount

///   memberCount × <see cref="SquadMemberWire.EmbeddedMask8PayloadSize"/> bytes (worldCharKey u64 + level/abilities/elo/role/ready/offline)





///

/// Mask bit 4 (0x04):

///   u64 leaderLookupKey → looked up in the member map built by mask 8, so it must equal one
///   of those keys exactly. The resolved member is the client's only notion of who leads: with
///   no match it shows neither the leader's controls nor any member's name, level or server.

/// </summary>

public static class SquadWireContract

{

  public const ushort OpcodeSelectList = 0x30B;

  public const ushort OpcodeCreate = 0x30C;

  public const ushort OpcodeLeaveMember = 0x313;

  public const byte SquadBaseMask = 0x0F;

}


