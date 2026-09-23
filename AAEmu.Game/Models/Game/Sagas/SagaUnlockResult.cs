namespace AAEmu.Game.Models.Game.Sagas;

/// <summary>Outcome of unlocking (buying) a saga group's chronicle info.</summary>
public enum SagaUnlockResult
{
    /// <summary>New record created; the group is now Active.</summary>
    Ok = 0,
    /// <summary>The character already held the record; nothing changed.</summary>
    AlreadyUnlocked = 1,
    /// <summary>The requested group is not in the shipped content; callers must fail loudly.</summary>
    UnknownGroup = 2
}
