namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Validation shared by the private-portal creation packet and the GM registration command.
/// The client supplies the coordinates and name; the server only accepts finite values and a name
/// before allocating a private-book id.
/// </summary>
public static class PrivatePortalCreationRules
{
    // The portal-book wire field is bounded to 128 bytes; names are measured as UTF-8 bytes.
    public const int MaxNameUtf8Bytes = 128;

    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && System.Text.Encoding.UTF8.GetByteCount(name) <= MaxNameUtf8Bytes;

    public static bool IsValid(string name, float x, float y, float z, float zRot) =>
        IsValidName(name)
        && float.IsFinite(x)
        && float.IsFinite(y)
        && float.IsFinite(z)
        && float.IsFinite(zRot);
}
