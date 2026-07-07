namespace AAEmu.Game.Models.Game.Crime;

public enum TrialStep
{
    /// <summary>0 - Actual trial has not started yet, waiting in court jail</summary>
    DefendantAwaitingTrial,
    /// <summary>1 - Initialize the trial, send crime records and stuff to defendant</summary>
    VerifyCriminalRecord,
    /// <summary>2 - Send out jury invites and wait for them</summary>
    AwaitingJurySummons,
    /// <summary>3 - Shows crime records to participants</summary>
    ConfirmCriminalRecord,
    /// <summary>4 - Defendant closing statement (asks to skip)</summary>
    ClosingStatement,
    /// <summary>5 - Jury decides verdict (shows selection box to jury)</summary>
    JuryVerdict,
    /// <summary>6 - Trial canceled => default sentence</summary>
    TrialCancelled,
    /// <summary>7 - Defendant plead Guilty</summary>
    PleadGuilty,
    /// <summary>8 - Trial has ended</summary>
    EndTrial,
    /// <summary>Last - Internal cleanup</summary>
    Cleanup,
    /// <summary>No longer active</summary>
    Invalid
}
