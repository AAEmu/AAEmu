using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Hero;

/// <summary>
/// The Hero Qualification Evaluation: turns the period's reputation into leadership.
/// </summary>
/// <remarks>
/// Scheduled from reputation_resets, which ships as 12 - the 12AM and 12PM the client's reputation rule
/// text names. See ReputationManager.Evaluate for what it does.
/// </remarks>
public class ReputationEvaluationTask : Task
{
    public override void Execute()
    {
        ReputationManager.Instance.Evaluate();
    }
}
