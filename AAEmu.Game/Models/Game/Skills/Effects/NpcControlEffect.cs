using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
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
}
