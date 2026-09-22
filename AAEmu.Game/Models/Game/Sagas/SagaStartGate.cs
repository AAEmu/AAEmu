namespace AAEmu.Game.Models.Game.Sagas;

/// <summary>
/// Result of evaluating whether a character may start a quest that belongs to a saga group.
/// <see cref="ChronicleInfoNeed"/> maps to <c>QuestStatusFailed.ChronicleInfoNeed</c>, the refusal
/// code the client renders when the saga's chronicle info record does not exist yet.
/// </summary>
public enum SagaStartGate
{
    /// <summary>The quest is not a saga-group member, or the character holds the group's record.</summary>
    Allowed = 0,
    /// <summary>Saga-group member whose chronicle info has not been bought yet.</summary>
    ChronicleInfoNeed = 1
}
