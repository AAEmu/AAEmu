using System.Numerics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Models.Game.AI.v2.Controls;

public class AiPathPoint
{
    public JVector Position { get; init; }
    public AiPathPointAction Action { get; init; }
    public string Param { get; init; }
}
