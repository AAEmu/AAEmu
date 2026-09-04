namespace AAEmu.Game.Models.Game.Squad;

public class Squad
{
    public uint Id { get; init; }
    public uint CatalogId { get; init; }

    /// <summary>Zone group behind <see cref="CatalogId"/>; the client resolves the member cap from it.</summary>
    public uint ZoneGroupId { get; init; }

    /// <summary>Instance selector as the client sent it, echoed back so its own lookups resolve.</summary>
    public SquadFieldType Field { get; init; }
    public SquadOpenType OpenType { get; set; }
    public bool PartyInvitation { get; set; }
    public string Explanation { get; set; } = "";
    public byte LimitLevel { get; set; }
    public int LimitGearScore { get; set; }
    public uint MaxMembers { get; set; }
    public uint LeaderCharacterId { get; set; }
    public bool IgnoreMinGameSize { get; set; }

    /// <summary>Queued for matching. The client shows the squad as registered while this holds.</summary>
    public bool MatchingApplied { get; set; }

    public bool Joining { get; set; }
    public bool EnterCommitted { get; set; }

    /// <summary>
    /// Set when the squad is handed into an instance. Survives leave-flag resets so a stale
    /// Quick Enter squad outside the dungeon can still be torn down on the next list refresh.
    /// </summary>
    public bool HasEnteredInstance { get; set; }

    public List<SquadMember> Members { get; } = [];

    public int MemberCount => Members.Count;

    public SquadMember? GetMember(uint characterId) =>
        Members.FirstOrDefault(m => m.CharacterId == characterId);

    /// <summary>
    /// Offline seats still occupy the roster. Disconnect marks <see cref="SquadMember.Offline"/>;
    /// the leader expels if they want the slot back. Auto-drop would let a stranger take a
    /// reconnecting member's seat.
    /// </summary>
    public bool IsFull => MaxMembers > 0 && Members.Count >= MaxMembers;

    public bool AllReady => SquadRules.AllOnlineReady(Members);
}
