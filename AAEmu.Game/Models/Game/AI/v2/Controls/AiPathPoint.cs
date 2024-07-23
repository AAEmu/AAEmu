using System.Numerics;

namespace AAEmu.Game.Models.Game.AI.v2.Controls;

public class AiPathPoint
{
    public Vector3 Position { get; set; }
    public AiPathPointAction Action { get; set; }
    public string Param { get; set; }
}
