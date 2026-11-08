using System.Numerics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Models.Game.Models;

public class GameStance
{
    public uint Id { get; set; }
    public GameStanceType StanceId { get; set; }
    public uint ActorModelId { get; set; }
    public string Name { get; set; } // Not really needed
    public float AiMoveSpeedRun { get; set; }
    public float AiMoveSpeedSlow { get; set; }
    public float AiMoveSpeedSprint { get; set; }
    public float AiMoveSpeedWalk { get; set; }
    public float HeightCollider { get; set; }
    public float HeightPivot { get; set; }
    public float NormalSpeed { get; set; }
    public float MaxSpeed { get; set; }
    public JVector Size { get; set; }
    public JVector ModelOffset { get; set; }
    public JVector ViewOffset { get; set; }
    public bool UseCapsule { get; set; }
}
