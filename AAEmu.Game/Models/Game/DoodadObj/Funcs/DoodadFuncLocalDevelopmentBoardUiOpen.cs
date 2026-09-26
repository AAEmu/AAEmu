using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Server-side representation of a local-development board descriptor.
/// </summary>
/// <remarks>
/// The descriptor carries one value: the board type the client reads when it opens the board window.
/// The window and its content are the client's own, so this interaction does not change doodad state
/// and does not send a board packet of its own. Shipping content declares this function
/// <see cref="DoodadFuncPermission.Public"/>, and <see cref="IsInteractionAllowed"/> refuses any other
/// permission rather than guessing at a rule this feature does not model; a refused player is told so
/// instead of pressing a key that appears to do nothing.
/// </remarks>
public sealed class DoodadFuncLocalDevelopmentBoardUiOpen : DoodadFuncTemplate
{
    /// <summary>Board type of the local development this doodad shows.</summary>
    public uint LocalDevelopmentBoardTypeId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (owner is null)
        {
            return;
        }

        if (!IsInteractionAllowed(owner.CurrentFuncs))
        {
            if (caster is Character character)
            {
                character.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
            }

            return;
        }

        // The client opens the board window from this descriptor; nothing is sent from here.
    }

    /// <summary>
    /// A board is opened only by the function row this template describes, and only while that row
    /// still declares the public permission content ships for every board.
    /// </summary>
    internal static bool IsInteractionAllowed(IEnumerable<DoodadFunc> currentFuncs)
    {
        return currentFuncs?.Any(func =>
            func.FuncType == nameof(DoodadFuncLocalDevelopmentBoardUiOpen) &&
            (DoodadFuncPermission)func.PermId == DoodadFuncPermission.Public) ?? false;
    }
}
