using System.Numerics;

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
    public Vector3 Size { get; set; }
    public Vector3 ModelOffset { get; set; }
    public Vector3 ViewOffset { get; set; }
    public bool UseCapsule { get; set; }
}
