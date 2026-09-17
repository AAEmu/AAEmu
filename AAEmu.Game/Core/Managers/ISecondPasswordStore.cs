using AAEmu.Game.Models.Game.SecondPassword;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Where a second password is kept between sessions. The password guards account-wide actions that outlive
/// a World process, so an in-memory only store would fail open after a restart.
/// </summary>
public interface ISecondPasswordStore
{
    /// <summary>The account's stored password, or null when the account has none.</summary>
    SecondPasswordRecord Load(uint accountId);

    /// <summary>Writes the account's password, replacing what was there.</summary>
    void Save(SecondPasswordRecord record);

    /// <summary>Removes the account's password.</summary>
    void Delete(uint accountId);
}
