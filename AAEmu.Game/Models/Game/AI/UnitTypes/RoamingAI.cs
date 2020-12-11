using AAEmu.Game.Models.Game.AI.Framework;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    public class RoamingAI : AbstractUnitAI
    {
        /*
         * The roaming AI has 4 goals:
         * - Move to target when in combat
         * - Use skills when in combat
         * - Roam when not in combat
         *
         * The roaming should also work when working with formations, that is, pack of mobs walking together.
         */
    }
}


/*
AIBehaviour.roaming = {
	Name = "roaming",
    alertness = AIALERTNESS_IDLE,

	--------------------------------------------------------------------------
	Constructor = function(self, entity)
        entity:ClearAICombat();
        X2AI:AttackReservedTarget(entity);

        AI.SetRefPointPosition(entity.id, entity.AI.idlePos);

        AI.BeginGoalPipe("roaming_Base");
            AI.PushGoal("bodypos", 1, BODYPOS_RELAX);
            AI.PushGoal("run", 1, 0);
            AI.PushGoal("useskill", 1, USF_AUTO_STICK);
            AI.PushGoal("signal", 1, AISIGNAL_INCLUDE_DISABLED, "UpdateRoaming", SIGNALFILTER_SENDER);
        AI.EndGoalPipe();

        entity:SelectPipe(AIGOALPIPE_DONT_RESET_AG, "roaming_Base");
	end,

	--------------------------------------------------------------------------
	--		Custom Event
	--------------------------------------------------------------------------
    UpdateRoaming = function(self, entity, sender, data)
        entity:SelectPipe(AIGOALPIPE_DONT_RESET_AG, "_first_");

        -- 1: Group member must go to its formation point
        if (X2AI:IsGroupMember(entity)) then
            PipeManager:Create_formation_GoToOnPoint(entity);
            entity:SelectPipe(AIGOALPIPE_DONT_RESET_AG, "formation_GoToOnPoint");
            return;
        end

        -- 2: Non-Group AI do "Roaming"
        X2AI:CalcNextRoamingPosition(entity);
        PipeManager:Create_roaming(entity);
        entity:SelectPipe(AIGOALPIPE_DONT_RESET_AG, "roaming");
    end,

    -----------------------------------------------------------
    UpdateIdleEnd = function(self, entity, sender, data)
        if (not X2AI:IsGroupMember(entity) and X2AI:IsOutOfIdleArea(entity)) then
            entity.unit:NpcTeleportTo(entity.AI.idlePos);
        end
        entity:SelectPipe(AIGOALPIPE_DONT_RESET_AG, "roaming_Base");
    end,

    -----------------------------------------------------------
    OnPathStuck = function(self, entity, sender, data)
        self:UpdateIdleEnd(entity, sender, data);
    end,

    --------------------------------------------------------------------------
    OnRequestSkillInfo = function(self, entity)
        entity.unit:NpcSetSkillList(entity.AI.skills.idle);
    end,

    -----------------------------------------------------------
    OnArrivedOnFormationPoint = function(self, entity, sender, data)
        self:UpdateIdleEnd(entity, sender, data);
    end,

    -----------------------------------------------------------
    OnAlertTargetChanged = function(self, entity, sender, data)
        local isGroupMember, leaderEntity = X2AI:IsGroupMember(entity);
        if (isGroupMember) then
            entity.AI.idlePos = leaderEntity:GetPos();
            entity.AI.idlePos.z = entity.AI.idlePos.z + 0.5;
        end

        AIBehaviour.DEFAULT:OnAlertTargetChanged(entity, sender, data);
    end,

    -----------------------------------------------------------
    OnAggroTargetChanged = function(self, entity, sender, data)
        local pos = entity:GetPos();
        AI.SetRefPointPosition(entity.id, pos);
        AI.SetAttentionTargetOf(entity.id, data.id, AITARGETREASON_ATTACK);
    end,
}
*/
