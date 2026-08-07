using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

/// <summary>
/// Inspects or overrides the contribution counter used by DoodadFuncDevote, so construction
/// content (Auroria bases, walls, bridges, purification monoliths) can be tested without
/// grinding out every contribution.
/// </summary>
public class DoodadDevoteSubCommand : SubCommandBase
{
    public DoodadDevoteSubCommand()
    {
        Title = "[Doodad Devote]";
        Description = "Show or set a doodad's contribution counter. Omit Count to just inspect. " +
                      "Setting it to one below the target lets the next real contribution trigger the phase change.";
        CallPrefix = $"{CommandManager.CommandPrefix}doodad devote";
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "Object Id", true));
        AddParameter(new NumericSubCommandParameter<int>("Count", "contributions made", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        uint doodadObjId = parameters["ObjId"];
        var doodad = ((Character)character).ParentWorld.GetDoodad(doodadObjId);
        if (doodad is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Doodad with objId {doodadObjId} does not exist");
            return;
        }

        // The current phase counts contributions through either an interactive DoodadFuncDevote or a
        // skill-driven DoodadFuncReactDevote. Work out which, so we can report the target and cost.
        var target = 0;
        var nextPhase = 0;
        var describeCost = "";

        foreach (var func in DoodadManager.Instance.GetFuncsForGroup(doodad.FuncGroupId))
        {
            if (func.FuncType != nameof(DoodadFuncDevote))
                continue;

            if (DoodadManager.Instance.GetFuncTemplate(func.FuncId, func.FuncType) is not DoodadFuncDevote devote)
                continue;

            target = devote.Count;
            // For an interactive devote the destination lives on the doodad_funcs row, not the template
            nextPhase = func.NextPhase;
            describeCost = $"costing {devote.ItemCount}x item {devote.ItemId} each, then phase {nextPhase}";
            break;
        }

        if (target == 0)
        {
            foreach (var phaseFunc in DoodadManager.Instance.GetPhaseFunc(doodad.FuncGroupId))
            {
                if (phaseFunc?.FuncType != nameof(DoodadFuncReactDevote))
                    continue;

                if (DoodadManager.Instance.GetPhaseFuncTemplate(phaseFunc.FuncId, phaseFunc.FuncType)
                    is not DoodadFuncReactDevote reactDevote)
                    continue;

                target = reactDevote.Count;
                nextPhase = reactDevote.NextPhase;
                describeCost = $"driven by hits of skill {reactDevote.SkillId}, then phase {nextPhase}";
                break;
            }
        }

        if (target == 0)
        {
            SendColorMessage(messageOutput, Color.Orange,
                $"Doodad {doodad.TemplateId} (objId {doodad.ObjId}) has no contribution counter on its current phase {doodad.FuncGroupId} - nothing to count");
            return;
        }

        if (parameters.TryGetValue("Count", out var newCountValue))
        {
            int newCount = newCountValue;
            if (newCount < 0)
            {
                SendColorMessage(messageOutput, Color.Red, "Count cannot be negative");
                return;
            }

            DoodadFuncDevote.PublishProgress(doodad, newCount);
            Logger.Warn($"{Title}: TemplateId {doodad.TemplateId}, objId {doodad.ObjId}, contributions set to {newCount}/{target}");

            // Reaching the target normally advances the doodad, but that happens inside DoFunc /
            // OnSkillHit which this command bypasses - so do it here too.
            if (newCount >= target)
            {
                DoodadFuncDevote.PublishProgress(doodad, 0);

                if (nextPhase > 0)
                {
                    SendMessage(messageOutput, $"Target reached, advancing to phase {nextPhase}");
                    Logger.Warn($"{Title}: TemplateId {doodad.TemplateId}, objId {doodad.ObjId}, advancing to phase {nextPhase}");
                    doodad.DoChangePhase((Unit)character, nextPhase);
                }
                else
                {
                    SendColorMessage(messageOutput, Color.Orange,
                        $"Target reached but this phase has no next phase ({nextPhase}) - counter reset, doodad unchanged");
                }

                return;
            }
        }

        SendMessage(messageOutput,
            $"TemplateId {doodad.TemplateId}, ObjId {doodad.ObjId}, phase {doodad.FuncGroupId}: " +
            $"{doodad.Data}/{target} contributions ({target - doodad.Data} left), " +
            describeCost + (doodad.IsPersistent ? " [persistent]" : " [not persistent - progress lost on restart]"));
    }
}
