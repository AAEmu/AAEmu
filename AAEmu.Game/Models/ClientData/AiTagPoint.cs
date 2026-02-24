using System.Numerics;

namespace AAEmu.Game.Models.ClientData;

/// <summary>
/// AI Smart Object tag point — defines a position + direction for AI interaction
/// (e.g., sit, patrol, guard). Loaded from tags.txt in zone folders.
/// </summary>
public class AiTagPoint
{
    public Vector3 Pos { get; set; }
    public Vector3 Dir { get; set; }
}
