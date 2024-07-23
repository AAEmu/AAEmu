namespace AAEmu.Game.Models.Game.AI.v2.Controls;

public enum AiPathPointAction
{
    /// <summary>
    /// No additional actions for this point
    /// </summary>
    None,
    /// <summary>
    /// Disables movement path loop (default behavior)
    /// </summary>
    DisableLoop,
    /// <summary>
    /// Enables movement path loop
    /// </summary>
    EnableLoop,
    /// <summary>
    /// Changes movement speed to be used when following the path
    /// </summary>
    Speed,
    /// <summary>
    /// Stance to be used when following the path
    /// </summary>
    StanceFlags,
    /// <summary>
    /// Inserted by RunCommandSet to part that it needs to return there, do not manually put this in .path files
    /// </summary>
    ReturnToCommandSet
}
