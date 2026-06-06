using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class NpcControlEffect : EffectTemplate
{
    public NpcControlCategory CategoryId { get; set; }
    public string ParamString { get; set; }
    public uint ParamInt { get; set; }

    // ---
    private string fileName { get; set; }
    private string fileName2 { get; set; }
    private uint skillId { get; set; }
    private uint timeout { get; set; }
    // ---

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info($"NpcControllEffect: CategoryId={CategoryId}, ParamString={ParamString}, ParamInt={ParamInt}, caster={caster.TemplateId}, target={target.TemplateId}");

        fileName = string.Empty;
        fileName2 = string.Empty;

        if (target is Npc targetNpc)
        {
            switch (CategoryId)
            {
                case NpcControlCategory.Signal:
                    break;
                case NpcControlCategory.FollowUnit:
                    break;
                case NpcControlCategory.FollowPath:
                    {
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
                    break;
                case NpcControlCategory.GoAway:
                    break;
                case NpcControlCategory.RunCommandSet:
                    {
                        var cmds = AiGameData.Instance.GetAiCommands(ParamInt);
                        if (cmds is { Count: > 0 })
                        {
                            // EnqueueAiCommands hands the cmd_set off to the AI driver
                            // (RunCommandSetBehavior → FollowPathBehavior). Previously we ALSO
                            // started Simulation.GoToPath here directly, which raced the AI path:
                            // both paths set MoveToPathEnabled, the second flip toggled it back
                            // off, and Simulation.StopMove froze the NPC mid-walk (Workflow
                            // diagnosis for Halcyona Golem: "wakes up, doesn't walk path").
                            // Let the AI win.
                            targetNpc.Ai?.EnqueueAiCommands(cmds);
                        }
                        break;
                    }
                default:
                    throw new NotSupportedException(nameof(CategoryId));
            }
        }
    }
}
