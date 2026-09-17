namespace AAEmu.Game.Models.Game.SecondPassword;

/// <summary>
/// A stored second password: the salt it was derived against and the derived key, never the password
/// itself. The failed count rides along so the throttle's history survives a restart.
/// </summary>
public sealed class SecondPasswordRecord
{
    public uint AccountId { get; init; }

    /// <summary>The per-password salt the key was derived with.</summary>
    public byte[] Salt { get; init; }

    /// <summary>The base64 derived key.</summary>
    public string Hash { get; init; }

    /// <summary>Wrong answers accumulated, which is what the client is told when one is given.</summary>
    public int FailedCount { get; init; }
}
