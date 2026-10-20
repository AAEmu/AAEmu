using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// Kind-7 rotate controller for the verified 180-degree post-leap profile. Other rotate value
/// signatures stay unsupported until their semantics are known.
/// </summary>
public sealed class RotateSkillController : SkillController
{
    // Profile match against skill_controllers.values — not a content id.
    private const int TurnDegrees = 180;

    public RotateSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target as Unit;
    }

    /// <summary>
    /// Only the complete four-value signature (180, 0, 100, 0) is supported, so other kind-7 rows
    /// are not guessed into this path.
    /// </summary>
    public static bool Supports(SkillControllerTemplate template) =>
        template?.Value is { Length: >= 4 } values &&
        values[0] == TurnDegrees && values[1] == 0 && values[2] == 100 && values[3] == 0;

    public override void Execute()
    {
        base.Execute();
        if (Owner == null)
        {
            End();
            return;
        }

        var local = Owner.Transform.Local;
        var rotation = local.Rotation;
        local.SetRotation(rotation.X, rotation.Y, MathF.IEEERemainder(rotation.Z + MathF.PI, MathF.Tau));
        Owner.Transform.FinalizeTransform();

        // One-shot movement state so server facing matches the plot turn for later cone checks.
        var world = Owner.Transform.World;
        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = world.Position.X;
        moveType.Y = world.Position.Y;
        moveType.Z = world.Position.Z;
        (moveType.RotationX, moveType.RotationY, moveType.RotationZ) = world.ToRollPitchYawSBytesMovement();
        moveType.ActorFlags = 5;
        moveType.DeltaMovement = [0, 0, 0];
        moveType.Stance = Owner.IsInBattle ? GameStanceType.Combat : GameStanceType.Relaxed;
        moveType.Alertness = Owner.IsInBattle ? MoveTypeAlertness.Combat : MoveTypeAlertness.Idle;
        moveType.Flags = Owner.IsInBattle ? MoveTypeFlags.InCombat : MoveTypeFlags.None;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds + 50;
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), true);

        End();
    }
}
