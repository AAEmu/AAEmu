using System.Text;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using NLog;

namespace AAEmu.Game.Core.Managers;

public enum ButlerRenameFailure
{
    None,
    InvalidName,
    NotBound,
    PersistenceFailed
}

public readonly record struct ButlerRenameResult(
    bool Success,
    ButlerRenameFailure Failure,
    string Name);

public interface IButlerRenameService
{
    ButlerRenameResult Rename(Character character, string requestedName);
}

/// <summary>Validates and durably changes the current character's bound farmhand name.</summary>
public sealed class ButlerRenameService : IButlerRenameService
{
    // tab_my_butler.lua applies 25 through SetMaxTextLength. The native request and response
    // string readers use a 0x80-byte UTF-8 bound.
    internal const int MinimumCharacterCount = 2;
    internal const int MaximumCharacterCount = 25;
    internal const int MaximumUtf8ByteCount = 0x80;
    internal const short NameUpdatedFlags = 0x10;

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IButlerManager _butlerManager;
    private readonly Action<CharacterButlerRecord> _persist;
    private readonly Action<Character, string> _publish;

    public ButlerRenameService(IButlerManager butlerManager, IButlerRepository repository)
        : this(butlerManager, record => Persist(repository, record), Publish)
    {
    }

    internal ButlerRenameService(
        IButlerManager butlerManager,
        Action<CharacterButlerRecord> persist,
        Action<Character, string> publish = null)
    {
        _butlerManager = butlerManager ?? throw new ArgumentNullException(nameof(butlerManager));
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _publish = publish ?? Publish;
    }

    public ButlerRenameResult Rename(Character character, string requestedName)
    {
        if (character == null || !TryNormalizeName(requestedName, out var name))
            return Failed(ButlerRenameFailure.InvalidName);

        var butler = _butlerManager.GetOrCreate(character.Id);
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                // Native isMine requires initialized Butler state owned by the active character.
                if (butler.IsDeleted || butler.CharacterId != character.Id || butler.HouseId == 0)
                    return Failed(ButlerRenameFailure.NotBound);
                if (string.Equals(butler.Name, name, StringComparison.Ordinal))
                {
                    _publish(character, name);
                    return Succeeded(name);
                }

                var proposed = butler.Snapshot() with { Name = name };
                try
                {
                    _persist(proposed);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to rename farmhand for character {0}", character.Id);
                    return Failed(ButlerRenameFailure.PersistenceFailed);
                }

                butler.Apply(proposed);
                _publish(character, name);
                return Succeeded(name);
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }
    }

    internal static bool TryNormalizeName(string requestedName, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(requestedName) || requestedName.IndexOf('\0') >= 0)
            return false;

        try
        {
            if (StrictUtf8.GetByteCount(requestedName) > MaximumUtf8ByteCount)
                return false;

            var characterCount = 0;
            foreach (var rune in requestedName.EnumerateRunes())
            {
                characterCount++;
                // The compact content does not include the native locale classifier or its
                // allowed-character dictionary. Keep the server-side en_us policy to the
                // conservative subset that can be classified without guessing another script.
                if (characterCount > MaximumCharacterCount || !IsEnglishLetterOrDigit(rune))
                    return false;
            }
            if (characterCount < MinimumCharacterCount)
                return false;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        name = requestedName;
        return true;
    }

    private static bool IsEnglishLetterOrDigit(Rune rune) =>
        rune.Value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static void Persist(IButlerRepository repository, CharacterButlerRecord record)
    {
        ArgumentNullException.ThrowIfNull(repository);
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        repository.Save(record, connection, transaction);
        transaction.Commit();
    }

    private static void Publish(Character character, string name) =>
        character.SendPacket(new SCButlerInfoUpdatedPacket(
            (ushort)ErrorMessageType.NoErrorMessage,
            NameUpdatedFlags,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            new Dictionary<sbyte, ulong>(),
            0,
            0,
            0,
            name,
            new Dictionary<uint, uint>()));

    private static ButlerRenameResult Failed(ButlerRenameFailure failure) =>
        new(false, failure, string.Empty);

    private static ButlerRenameResult Succeeded(string name) =>
        new(true, ButlerRenameFailure.None, name);
}
