namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class BuffTolerance
{
    public uint Id { get; set; }
    public uint BuffTagId { get; set; }
    // In seconds, not sure how that one works
    public uint StepDuration { get; set; }
    // Immunity if triggered too often
    public uint FinalStepBuffId { get; set; }
    // Reduction in % for sleep etc.. in PVP
    public uint CharacterTimeReduction { get; set; }

    public List<BuffToleranceStep> Steps { get; set; }

    public BuffToleranceStep GetFirstStep()
    {
        return Steps.First();
    }

    /// <summary>
    /// The step that follows <paramref name="step"/> in the progression, or the last step when
    /// <paramref name="step"/> is already on it.
    /// </summary>
    /// <remarks>
    /// Clamping is the point, not a fallback: the caller reads "there is no step left that reduces any
    /// more" as the signal to hand out <see cref="FinalStepBuffId"/>, and the old <c>First</c> threw
    /// <c>InvalidOperationException</c> on the application that reached the end of the ladder.
    /// </remarks>
    public BuffToleranceStep GetStepAfter(BuffToleranceStep step)
    {
        return Steps.FirstOrDefault(st => st.Id > step.Id) ?? Steps[^1];
    }
}
