using System;
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
        if ((Ai.AiCurrentCommand != null) || (Ai.AiCommandsQueue.Count > 0))
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
                Ai.LoadAiPathPoints(Ai.AiFileName, aiCommand.Param1 == 1);
                if (aiCommand.Param1 == 1)
                {
                    Ai.PathHandler.AiPathPointsRemaining.Enqueue(new AiPathPoint() { Position = Vector3.Zero, Action = AiPathPointAction.ReturnToCommandSet, Param = string.Empty });
                }
                Ai.GoToFollowPath();
                if (aiCommand.Param1 == 1)
                {
                    Ai.AiFileName = aiCommand.Param2;
                }
                else
                {
                    Ai.AiFileName2 = aiCommand.Param2;
                }

                break;
            case AiCommandCategory.UseSkill:
                Ai.AiSkillId = aiCommand.Param1;
                var skillTemplate = SkillManager.Instance.GetSkillTemplate(Ai.AiSkillId);
                if (skillTemplate != null && Ai.Owner.UseSkill(Ai.AiSkillId, Ai.Owner.CurrentTarget as Unit ?? Ai.Owner) == SkillResult.Success)
                {
                    var coolDown = SkillManager.GetAttackDelay(skillTemplate, Ai.Owner, false, 0.0);
                    Ai.AiCurrentCommandRunTime = TimeSpan.FromMilliseconds(coolDown);
                }
                break;
            case AiCommandCategory.Timeout:
                Ai.AiTimeOut = aiCommand.Param1;
                Ai.AiCurrentCommandRunTime = TimeSpan.FromMilliseconds(Ai.AiTimeOut); // This feels like this should be max cooldown remaining for all skills for param 1
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
