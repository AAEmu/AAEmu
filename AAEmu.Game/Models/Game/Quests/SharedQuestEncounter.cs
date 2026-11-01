namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Tracks a shared combat encounter independently of NPC and quest side effects.
/// </summary>
internal sealed class SharedQuestEncounter
{
    internal enum Stage { Combat, Dialogue, Resetting, Finished }

    private readonly HashSet<uint> _allowedPlayers;
    private readonly HashSet<uint> _participants = [];
    private readonly HashSet<uint> _completed = [];
    private readonly TimeSpan _combatTimeout;
    private readonly TimeSpan _dialogueTimeout;
    private readonly TimeSpan _resetDelay;

    public uint OwnerId { get; }
    public Stage CurrentStage { get; private set; } = Stage.Combat;
    public DateTime Deadline { get; private set; }
    public IEnumerable<uint> UnfinishedParticipants => _participants.Except(_completed);
    public bool OwnerCompleted => _completed.Contains(OwnerId);

    public SharedQuestEncounter(uint ownerId, IEnumerable<uint> allowedPlayers, DateTime now,
        TimeSpan combatTimeout, TimeSpan dialogueTimeout, TimeSpan resetDelay)
    {
        OwnerId = ownerId;
        _allowedPlayers = [.. allowedPlayers, ownerId];
        _combatTimeout = combatTimeout;
        _dialogueTimeout = dialogueTimeout;
        _resetDelay = resetDelay;
        Deadline = now + combatTimeout;
    }

    public bool TryJoin(uint playerId)
    {
        return CurrentStage == Stage.Combat && _allowedPlayers.Contains(playerId) && _participants.Add(playerId);
    }

    public bool CanDamage(uint playerId) => CurrentStage == Stage.Combat && _participants.Contains(playerId);
    public bool CanTalk(uint playerId) => CurrentStage == Stage.Dialogue && _participants.Contains(playerId);

    public void RemoveParticipant(uint playerId) => _participants.Remove(playerId);

    public void RecordDamage(uint playerId, DateTime now)
    {
        if (CanDamage(playerId))
            Deadline = now + _combatTimeout;
    }

    public bool BeginDialogue(DateTime now)
    {
        if (CurrentStage != Stage.Combat)
            return false;
        CurrentStage = Stage.Dialogue;
        Deadline = now + _dialogueTimeout;
        return true;
    }

    public void Complete(uint playerId)
    {
        if (CanTalk(playerId))
            _completed.Add(playerId);
    }

    public void BeginReset(DateTime now)
    {
        if (CurrentStage is Stage.Resetting or Stage.Finished)
            return;
        CurrentStage = Stage.Resetting;
        Deadline = now + _resetDelay;
    }

    public void Tick(DateTime now, bool ownerAvailable)
    {
        if (CurrentStage == Stage.Finished)
            return;
        if (CurrentStage == Stage.Resetting)
        {
            if (now >= Deadline)
                CurrentStage = Stage.Finished;
            return;
        }

        if ((!ownerAvailable && !OwnerCompleted) || now >= Deadline ||
            (CurrentStage == Stage.Dialogue && _participants.All(_completed.Contains)))
        {
            BeginReset(now);
        }
    }
}
