using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>Read-only diagnostics for the audited skill content switches.</summary>
public class SkillFields : ICommand
{
    public string[] CommandNames { get; set; } = ["skillfields", "skill_fields", "skillfieldaudit"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[summary | links <count> | <skill id>]";
    }

    public string GetCommandHelpText()
    {
        return "Read-only audit of skill content switches and their enum_equip_slot links";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var manager = SkillManager.Instance;
        var mode = args.Length == 0 ? "summary" : args[0];

        if (string.Equals(mode, "summary", StringComparison.OrdinalIgnoreCase))
        {
            var report = manager.GetSkillContentFieldAudit();
            messageOutput.SendMessage(
                $"skills={report.TotalSkills} auto_fire={report.AutoFireCount} " +
                $"sensitive_operation={report.SensitiveOperationCount} " +
                $"valid_height_edge_to_edge={report.ValidHeightEdgeToEdgeCount} " +
                $"linked={report.LinkedSkillCount} no_link={report.NoLinkSkillCount} " +
                $"unknown_link={report.UnknownLinkSkillIds.Count}");
            return;
        }

        if (string.Equals(mode, "links", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 2 || !int.TryParse(args[1], out var count) || count < 0)
            {
                CommandManager.SendErrorText(this, messageOutput, "links requires a non-negative row count");
                return;
            }

            var links = manager.GetSkillContentFieldAudit().Links;
            foreach (var link in links.Take(count))
            {
                messageOutput.SendMessage(
                    $"skill={link.SkillId} slot={link.EquipSlotId} " +
                    $"slot_name={link.EquipSlotName} category={link.EquipSlotCategory ?? "none"}");
            }
            return;
        }

        if (!uint.TryParse(mode, out var skillId))
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var skill = manager.GetSkillTemplate(skillId);
        if (skill == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"skill {skillId} is not loaded");
            return;
        }

        var linkedSlot = manager.TryGetLinkedEquipSlot(skillId, out var slot)
            ? $"slot={slot.Id} slot_name={slot.Name} category={slot.Category ?? "none"}"
            : "slot=none";
        messageOutput.SendMessage(
            $"skill={skill.Id} auto_fire={skill.AutoFire} sensitive_operation={skill.SensitiveOperation} " +
            $"valid_height_edge_to_edge={skill.ValidHeightEdgeToEdge} valid_height={skill.ValidHeight} " +
            $"target_valid_height={skill.TargetValidHeight} {linkedSlot}");
    }
}
