using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class NpcControlEffect : EffectTemplate
    {
        public NpcControlCategory CategoryId { get; set; }
        public string ParamString { get; set; }
        public uint ParamInt { get; set; }

        // ---
        private string fileName { get; set; }
        private uint skillId { get; set; }
        private uint timeout { get; set; }
        // ---

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug($"NpcControllEffect: CategoryId={CategoryId}, ParamString={ParamString}, ParamInt={ParamInt}");

            if (caster is Npc npc)
            {
                switch (CategoryId)
                {
                    case NpcControlCategory.Signal:
                        break;
                    case NpcControlCategory.FollowUnit:
                        break;
                    case NpcControlCategory.FollowPath:
                        {
                            if (npc.IsInPatrol) { break; }
                            npc.IsInPatrol = true;
                            npc.Simulation.RunningMode = false;
                            npc.Simulation.MoveToPathEnabled = false;
                            npc.Simulation.MoveFileName = ParamString;
                            npc.Simulation.GoToPath(npc, true);
                            break;
                        }
                    case NpcControlCategory.AttackUnit:
                        npc.SetFaction(FactionsEnum.Monstrosity);
                        return;
                    case NpcControlCategory.GoAway:
                        break;
                    case NpcControlCategory.RunCommandSet:
                        {
                            var cmds = AiGameData.Instance.GetAiCommands(ParamInt);
                            if (cmds is { Count: > 0 })
                            {
                                foreach (var aiCommands in cmds)
                                {
                                    switch (aiCommands.CmdId)
                                    {
                                        case AiCommandCategory.FollowUnit:
                                            break;
                                        case AiCommandCategory.FollowPath:
                                            {
                                                if (aiCommands.Param1 == 1) // TODO может быть несколько маршрутов, используем первый // there may be several routes, we use the first one
                                                    fileName = aiCommands.Param2;
                                                break;
                                            }
                                        case AiCommandCategory.UseSkill:
                                            skillId = aiCommands.Param1;
                                            break;
                                        case AiCommandCategory.Timeout:
                                            timeout = aiCommands.Param1;
                                            break;
                                        default:
                                            throw new ArgumentOutOfRangeException();
                                    }
                                }
                                if (!string.IsNullOrEmpty(fileName))
                                {
                                    if (npc.IsInPatrol) { return; }
                                    npc.IsInPatrol = true;
                                    npc.Simulation.RunningMode = false;
                                    npc.Simulation.Cycle = false;
                                    npc.Simulation.Remove = true;
                                    npc.Simulation.MoveToPathEnabled = false;
                                    npc.Simulation.MoveFileName = fileName;
                                    npc.Simulation.GoToPath(npc, true, skillId, timeout);
                                }
                            }
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            if (caster is Character && target is Npc npc2)
            {
                switch (CategoryId)
                {
                    case NpcControlCategory.Signal:
                        break;
                    case NpcControlCategory.FollowUnit:
                        break;
                    case NpcControlCategory.FollowPath:
                        {
                            if (npc2.IsInPatrol) { break; }
                            npc2.IsInPatrol = true;
                            npc2.Simulation.RunningMode = false;
                            npc2.Simulation.MoveToPathEnabled = false;
                            npc2.Simulation.MoveFileName = ParamString;
                            npc2.Simulation.GoToPath(npc2, true);
                            break;
                        }
                    case NpcControlCategory.AttackUnit:
                        npc2.SetFaction(FactionsEnum.Monstrosity);
                        break;
                    case NpcControlCategory.GoAway:
                        break;
                    case NpcControlCategory.RunCommandSet:
                        {
                            var cmds = AiGameData.Instance.GetAiCommands(ParamInt);
                            if (cmds is { Count: > 0 })
                            {
                                foreach (var aiCommands in cmds)
                                {
                                    switch (aiCommands.CmdId)
                                    {
                                        case AiCommandCategory.FollowUnit:
                                            break;
                                        case AiCommandCategory.FollowPath:
                                            fileName = aiCommands.Param2;
                                            break;
                                        case AiCommandCategory.UseSkill:
                                            skillId = aiCommands.Param1;
                                            break;
                                        case AiCommandCategory.Timeout:
                                            timeout = aiCommands.Param1;
                                            break;
                                        default:
                                            throw new ArgumentOutOfRangeException();
                                    }
                                }
                                if (!string.IsNullOrEmpty(fileName))
                                {
                                    if (npc2.IsInPatrol) { return; }
                                    npc2.IsInPatrol = true;
                                    npc2.Simulation.RunningMode = false;
                                    npc2.Simulation.Cycle = false;
                                    npc2.Simulation.MoveToPathEnabled = false;
                                    npc2.Simulation.MoveFileName = fileName;
                                    npc2.Simulation.GoToPath(npc2, true, skillId, timeout);
                                }
                            }
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}
