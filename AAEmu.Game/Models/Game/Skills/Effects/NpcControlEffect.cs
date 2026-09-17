using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.StaticValues;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class NpcControlEffect : EffectTemplate
{
    public NpcControlCategory CategoryId { get; set; }
    public string ParamString { get; set; }
    public uint ParamInt { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info($"NpcControllEffect: CategoryId={CategoryId}, ParamString={ParamString}, ParamInt={ParamInt}, caster={caster.TemplateId}, target={target.TemplateId}");

        if (target is Npc targetNpc)
        {
            var playerId = caster is Character ch ? ch.ObjId : 0u;
            switch (CategoryId)
            {
                case NpcControlCategory.Signal:
                    // No server behaviour, and deliberately none invented. The category carries only
                    // param_int/param_string (npc_control_effects has one row, category 0), and the zone has
                    // no Signal family to relay to: RelayQuestNpcAiToZone maps kind 0/1/2/3 to WZAttackOnQuest,
                    // WZFollowUnitOnQuest, WZFollowPathOnQuest and WZRunCommandSetOnQuest, and Quest.cs maps
                    // QuestNpcAiName.Signal to nothing as well. A Signal is a client-side cue; there is nothing
                    // here that a wrong guess would not break.
                    Logger.Debug("NpcControlEffect Signal on npc {0}: no server-side behaviour (param {1})",
                        targetNpc.ObjId, ParamInt);
                    break;
                case NpcControlCategory.FollowUnit:
                    if (WorldIntegration.ZoneAuthority && playerId != 0)
                        WorldIntegration.RelayQuestNpcAiToZone?.Invoke(1, targetNpc.ObjId, playerId, null, 0, 0);
                    break;
                case NpcControlCategory.FollowPath:
                    {
                        if (WorldIntegration.ZoneAuthority)
                        {
                            WorldIntegration.RelayQuestNpcAiToZone?.Invoke(
                                2, targetNpc.ObjId, playerId, ParamString ?? "", 0, 0);
                            break;
                        }
                        if (targetNpc.IsInPatrol) { break; }
                        targetNpc.IsInPatrol = true;
                        if (targetNpc.Simulation != null)
                        {
                            targetNpc.Simulation.RunningMode = false;
                            targetNpc.Simulation.MoveToPathEnabled = false;
                            targetNpc.Simulation.MoveFileName = ParamString;
                            targetNpc.Simulation.GoToPath(targetNpc, true);
                        }

                        break;
                    }
                case NpcControlCategory.AttackUnit:
                    targetNpc.SetFaction(FactionsEnum.Monstrosity);
                    if (WorldIntegration.ZoneAuthority && playerId != 0)
                        WorldIntegration.RelayQuestNpcAiToZone?.Invoke(0, targetNpc.ObjId, playerId, null, 0, 0);
                    break;
                case NpcControlCategory.GoAway:
                    // Undoes the two "come with me" categories above: a following or pathing npc stops where
                    // it stands. There is no zone relay for it — the four kinds RelayQuestNpcAiToZone carries
                    // are attack, follow-unit, follow-path and command-set — so this only clears the
                    // server-side follow/path state, which is what the World owns.
                    StopFollowing(targetNpc);
                    break;
                case NpcControlCategory.RunCommandSet:
                    // ai_command_sets: Zone loads from compact.sqlite3 and runs against its NPC.
                    if (WorldIntegration.ZoneAuthority)
                        WorldIntegration.RelayQuestNpcAiToZone?.Invoke(
                            3, targetNpc.ObjId, playerId, null, 0, (int)ParamInt);
                    else
                        Logger.Debug(
                            "NpcControlEffect RunCommandSet {0} on npc {1} is driven by the Zone",
                            ParamInt, targetNpc.ObjId);
                    break;
                default:
                    throw new NotSupportedException(nameof(CategoryId));
            }
        }
    }

    /// <summary>
    /// Ends the follow/path a <see cref="NpcControlCategory.FollowUnit"/> or
    /// <see cref="NpcControlCategory.FollowPath"/> row started.
    /// </summary>
    /// <remarks>
    /// <c>Simulation.MoveToPathEnabled</c> is the flag the route tick reads before it walks a path, and
    /// <c>IsInPatrol</c> is the guard the FollowPath case checks before it starts one, so clearing both is what
    /// stops the follow server-side. <c>PauseMove</c> and not <c>StopMove</c>: StopMove also honours the
    /// simulation's own <c>Remove</c>/<c>Timeout</c>, which belong to the path that was running and would
    /// despawn an npc GoAway only meant to halt.
    /// </remarks>
    private static void StopFollowing(Npc npc)
    {
        npc.IsInPatrol = false;

        if (npc.Simulation == null)
            return;

        npc.Simulation.MoveToPathEnabled = false;
        npc.Simulation.MoveFileName = string.Empty;
        Simulation.PauseMove(npc);
    }
}
