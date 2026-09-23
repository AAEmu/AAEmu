namespace AAEmu.Game.Models.Game.CrossServer;

/// <summary>
/// In-memory stand-in for the two tables the transfer state machine touches
/// (<c>characters</c> + <c>items</c>, and <c>character_transfer_journals</c>). It keeps the same
/// atomicity contract as the MySQL store — every mutator is one indivisible step — so the
/// state-machine tests exercise the real preconditions instead of a simplified double.
/// </summary>
public sealed class InMemoryCrossServerTransferStore : ICrossServerTransferStore
{
    /// <summary>A test-visible characters row: the wallet, the park marker and the item ids.</summary>
    public sealed class CharacterRow
    {
        public ulong CharacterId { get; init; }
        public uint AccountId { get; set; }
        public long Money { get; set; }
        public long Money2 { get; set; }
        public long AaPoint { get; set; }
        public List<long> ItemIds { get; } = [];
        public DateTime TransferRequestTime { get; set; }
    }

    private readonly Dictionary<ulong, CharacterRow> _characters = [];
    private readonly Dictionary<ulong, CrossServerTransferJournal> _journals = [];

    /// <summary>How many times a departure actually stamped the park marker — "exactly once" evidence.</summary>
    public int ParkMarkerWrites { get; private set; }

    /// <summary>How many restores (rollback / re-entry / recovery) wrote character state.</summary>
    public int RestoreWrites { get; private set; }

    public IReadOnlyDictionary<ulong, CharacterRow> Characters => _characters;
    public IReadOnlyDictionary<ulong, CrossServerTransferJournal> Journals => _journals;

    /// <summary>Seeds a characters row the way a real account would have one before any departure.</summary>
    public CharacterRow AddCharacter(ulong characterId, uint accountId, long money, long money2, long aaPoint, IEnumerable<long> itemIds)
    {
        var row = new CharacterRow
        {
            CharacterId = characterId,
            AccountId = accountId,
            Money = money,
            Money2 = money2,
            AaPoint = aaPoint,
        };
        row.ItemIds.AddRange(itemIds);
        _characters[characterId] = row;
        return row;
    }

    private long ItemCount(CharacterRow row) => row.ItemIds.Count;

    private long ItemFingerprint(CharacterRow row) => row.ItemIds.Sum(id => id);

    public CrossServerCharacterSnapshot CaptureSnapshot(ulong characterId)
    {
        if (!_characters.TryGetValue(characterId, out var row))
            return null;

        return new CrossServerCharacterSnapshot(
            characterId,
            row.Money,
            row.Money2,
            row.AaPoint,
            ItemCount(row),
            ItemFingerprint(row),
            row.TransferRequestTime);
    }

    public bool TryParkAndJournal(CrossServerTransferJournal journal)
    {
        if (journal.State != CrossServerTransferState.Parked)
            return false;

        if (!_characters.TryGetValue(journal.CharacterId, out var row))
            return false;

        // A live journal blocks the departure; a terminal one is history and is replaced.
        if (_journals.TryGetValue(journal.CharacterId, out var existing))
        {
            if (existing.State is CrossServerTransferState.Parked or CrossServerTransferState.Transferred)
                return false;
            _journals.Remove(journal.CharacterId);
        }

        // Park: stamp the characters row and keep the journal — one step, no partial parking.
        row.TransferRequestTime = journal.UpdatedUtc;
        ParkMarkerWrites++;
        _journals[journal.CharacterId] = journal;
        return true;
    }

    public CrossServerTransferJournal Get(ulong characterId) =>
        _journals.TryGetValue(characterId, out var journal) ? journal : null;

    public bool TrySetState(ulong characterId, CrossServerTransferState expected, CrossServerTransferState next)
    {
        if (!_journals.TryGetValue(characterId, out var journal) || journal.State != expected)
            return false;

        _journals[characterId] = journal with { State = next, UpdatedUtc = DateTime.UtcNow };
        return true;
    }

    public bool TryRestore(ulong characterId, CrossServerCharacterSnapshot snapshot, CrossServerTransferState expected, CrossServerTransferState next)
    {
        if (!_journals.TryGetValue(characterId, out var journal) || journal.State != expected)
            return false;

        if (!_characters.TryGetValue(characterId, out var row))
            return false;

        // Prove the inventory before touching anything: a mismatch refuses with nothing written,
        // which is what "no partial state" means here.
        if (ItemCount(row) != snapshot.ItemCount || ItemFingerprint(row) != snapshot.ItemFingerprint)
            return false;

        row.Money = snapshot.Money;
        row.Money2 = snapshot.Money2;
        row.AaPoint = snapshot.AaPoint;
        row.TransferRequestTime = snapshot.TransferRequestUtc;
        RestoreWrites++;

        _journals[characterId] = journal with { State = next, UpdatedUtc = DateTime.UtcNow };
        return true;
    }

    public IReadOnlyList<CrossServerTransferJournal> LoadAll() =>
        _journals.Values.OrderBy(j => j.CharacterId).ToList();
}
