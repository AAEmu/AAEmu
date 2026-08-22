using System.Numerics;

namespace AAEmu.Game.Models.Game;

/// <summary>
/// State kept per-character by the <c>/pathrec</c> GM command while a path is being recorded.
/// Lives outside <c>AAEmu.Game/Scripts/</c> on purpose: the runtime <c>ScriptCompiler</c> renames
/// every class it finds under that folder to <c>Generated_*</c>, which breaks cross-file type
/// matching for helper types declared alongside a script command. Keeping the helper here
/// preserves the original name everywhere.
/// </summary>
public sealed class PathRecordingSession
{
    public string Name;
    public uint CharacterObjId;
    public List<Vector3> Points;
    public DateTime LastSampleAt;
}
