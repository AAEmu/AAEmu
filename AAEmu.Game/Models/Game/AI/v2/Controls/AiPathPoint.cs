using System.Numerics;

namespace AAEmu.Game.Models.Game.AI.v2.Controls;

public class AiPathPoint
{
    public Vector3 Position { get; init; }
    public AiPathPointAction Action { get; init; }
    public string Param { get; init; }
}
