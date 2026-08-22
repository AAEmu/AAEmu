using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class RunCommandSetBehavior : BaseCombatBehavior
{
    public override void Enter()
    {
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Combat;
    }

    public override void Tick(TimeSpan delta)
    {
        // If everything is executed, and still time remaining, wait for it before going back to Neutral
        if (Ai.AiCurrentCommandRunTime > TimeSpan.Zero)
        {
            Ai.AiCurrentCommandRunTime -= delta;
            return;
        }

        // If there are commands in the AI Command queue, execute those first
        if (Ai.AiCurrentCommand != null || Ai.AiCommandsQueue.Count > 0)
        {
            if (Ai.AiCurrentCommand == null)
            {
                Ai.AiCurrentCommand = Ai.AiCommandsQueue.Dequeue();
                Ai.AiCurrentCommandStartTime = DateTime.UtcNow;
            }

            TickCurrentAiCommand(Ai.AiCurrentCommand, delta);
            return;
        }

        Ai.GoToIdle();
        // Ai.GoToDefaultBehavior();
    }

    public override void Exit()
    {
        //
    }

    /// <summary>
    /// Ticks the current AI command
    /// </summary>
    /// <param name="aiCommand"></param>
    /// <param name="delta"></param>
    /// <exception cref="NotSupportedException"></exception>
    private void TickCurrentAiCommand(AiCommands aiCommand, TimeSpan delta)
    {
        if (Ai.AiCurrentCommandRunTime < TimeSpan.Zero)
        {
            Ai.AiCurrentCommand = null;
            Ai.AiCurrentCommandRunTime = TimeSpan.Zero;
            return;
        }

        // Check if we're still waiting
        if (Ai.AiCurrentCommandRunTime > TimeSpan.Zero)
        {
            Ai.AiCurrentCommandRunTime -= delta;
            return;
        }

        Logger.Debug($"{Ai.Owner.ObjId} ({Ai.Owner.TemplateId}) executing AI Command: {aiCommand.CmdId}, CommandSet: {aiCommand.CmdSetId}, P1: {aiCommand.Param1}, P2: {aiCommand.Param2}");
        // Execute command
        switch (aiCommand.CmdId)
        {
            case AiCommandCategory.FollowUnit:
                Logger.Warn($"AI Command: {aiCommand.CmdId} not implemented, NPC {Ai.Owner.ObjId} ({Ai.Owner.TemplateId}), CommandSet {aiCommand.CmdSetId}, P1 {aiCommand.Param1}, P2 {aiCommand.Param2}");
                break;
            case AiCommandCategory.FollowPath:
                // Order fix: AiFileName MUST be set BEFORE LoadAiPathPoints, otherwise the loader
                // reads the previous-iteration filename (or string.Empty) and the path is empty.
                // Halcyona Golem ai_commands have Param2 = "nuia_golem_move" / "harihara_golem_move"
                // — without this fix, FollowPathBehavior sees zero points and goes straight to Idle.
                if (aiCommand.Param1 == 1)
                    Ai.AiFileName = aiCommand.Param2;
                else
                    Ai.AiFileName2 = aiCommand.Param2;

                Ai.LoadAiPathPoints(Ai.AiFileName, aiCommand.Param1 == 1);
                if (aiCommand.Param1 == 1)
                {
                    Ai.PathHandler.AiPathPointsRemaining.Enqueue(new AiPathPoint { Position = Vector3.Zero, Action = AiPathPointAction.ReturnToCommandSet, Param = string.Empty });
                }
                Ai.GoToFollowPath();
                break;
            case AiCommandCategory.UseSkill:
                Ai.AiSkillId = aiCommand.Param1;
                var owner = Ai.Owner; // capture once to avoid race with concurrent unit despawn
                var skillTemplate = SkillManager.Instance.GetSkillTemplate(Ai.AiSkillId);
                if (owner != null && skillTemplate != null && owner.UseSkill(Ai.AiSkillId, owner.CurrentTarget as Unit ?? owner) == SkillResult.Success)
                {
                    var coolDown = SkillManager.GetAttackDelay(skillTemplate, owner, false, 0.0);
                    Ai.AiCurrentCommandRunTime = TimeSpan.FromMilliseconds(coolDown);
                }
                break;
            case AiCommandCategory.Timeout:
                Ai.AiTimeOut = aiCommand.Param1;
                // ai_commands.param1 unit is overwhelmingly retail-seconds: 453 of 454 timeout
                // rows in the vanilla DB have Param1 in [1, 60] (avg 8.44), which is only
                // sensible as seconds — Param1=20ms would expire before the path engine even
                // started, leaving the AI falling out of the command set immediately. Halcyona
                // golem cmd_set 322/323 has Param1=20 (= 20s of immobilize before march), which
                // matched retail timing once we switched from FromMilliseconds.
                //
                // The single outlier is cmd_set 167 "상단 습격단 갱" (Merchant Raid Gang) with
                // Param1=1500, which looks legacy-ms-encoded (= 1.5s) — 25 minutes of waiting
                // before its next skill cast would freeze the NPC. Heuristic: Param1 ≥ 100
                // falls back to milliseconds; everything below treats it as seconds. The
                // threshold sits well above the second-encoded retail range (max 60) and
                // safely below the legacy-ms-encoded outlier. Audit was driven by Greptile
                // PR #1447 P1 review. If more outliers surface we can promote them per-cmd_set.
                Ai.AiCurrentCommandRunTime = Ai.AiTimeOut >= 100
                    ? TimeSpan.FromMilliseconds(Ai.AiTimeOut)
                    : TimeSpan.FromSeconds(Ai.AiTimeOut);
                break;
            default:
                throw new NotSupportedException(nameof(aiCommand.CmdId));
        }

        /*
        if (!string.IsNullOrEmpty(Ai.AiFileName))
        {
            if (Ai.Owner.IsInPatrol) { return; }

            Ai.Owner.IsInPatrol = true;
            Ai.Owner.Simulation.RunningMode = false;
            Ai.Owner.Simulation.Cycle = false;
            Ai.Owner.Simulation.MoveToPathEnabled = false;
            Ai.Owner.Simulation.MoveFileName = Ai.AiFileName;
            Ai.Owner.Simulation.MoveFileName2 = Ai.AiFileName2;
            Ai.Owner.Simulation.GoToPath(Ai.Owner, true, Ai.AiSkillId, Ai.AiTimeOut);
        }
        */

        if (Ai.AiCurrentCommandRunTime == TimeSpan.Zero)
            Ai.AiCurrentCommandRunTime = TimeSpan.FromSeconds(-1);
    }

}
