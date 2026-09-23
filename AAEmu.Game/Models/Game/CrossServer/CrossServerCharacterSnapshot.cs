using System.Text.Json;

namespace AAEmu.Game.Models.Game.CrossServer;

/// <summary>
/// The character state a cross-server departure freezes before it parks the character.
/// Rollback and re-entry restore exactly these fields, so a failed transfer cannot leave a
/// half-migrated character behind.
/// <para>
/// <see cref="Money"/>, <see cref="Money2"/> and <see cref="AaPoint"/> mirror the
/// <c>characters</c> columns of the same names; <see cref="ItemCount"/> /
/// <see cref="ItemFingerprint"/> witness the character's <c>items</c> rows
/// (<c>COUNT(*), SUM(id) WHERE owner = character</c>) so a rollback can prove the inventory it
/// snapshotted is still the inventory present, instead of restoring money over a changed one.
/// </para>
/// </summary>
public sealed record CrossServerCharacterSnapshot(
    ulong CharacterId,
    long Money,
    long Money2,
    long AaPoint,
    long ItemCount,
    long ItemFingerprint,
    DateTime TransferRequestUtc)
{
    private static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.General);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Deserializes a journal snapshot. A malformed row is a loud failure, never a default.</summary>
    /// <exception cref="InvalidDataException">The stored JSON is empty or not a snapshot.</exception>
    public static CrossServerCharacterSnapshot FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Cross-server transfer snapshot is empty.");

        try
        {
            return JsonSerializer.Deserialize<CrossServerCharacterSnapshot>(json, Options)
                   ?? throw new InvalidDataException("Cross-server transfer snapshot deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Cross-server transfer snapshot is malformed.", ex);
        }
    }
}
