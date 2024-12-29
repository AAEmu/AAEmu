using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class FindObject : ICommand
{
    public string[] CommandNames { get; set; } = ["findobject", "findobj"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<doodad || npc || gimmick> <template id> [max results]";
    }

    public string GetCommandHelpText()
    {
        return "Finds the closest object of the giver type and template id.";
    }

    private static int ShowObjectData(Character character, GameObject go, int index, string indexPrefix, IMessageOutput messageOutput)
    {
        var indexStr = indexPrefix;
        if (indexStr != string.Empty)
        {
            indexStr += " . ";
        }

        indexStr += (index + 1).ToString();

        if (go is Gimmick gGimmick)
        {
            messageOutput.SendMessage($"#{indexStr} -> BcId: {gGimmick.ObjId}, GimmickId: {gGimmick.GimmickId}, TemplateId: {gGimmick.TemplateId} - Model: {gGimmick.Template?.ModelPath}");
        }
        else
        if (go is Doodad gDoodad)
        {
            messageOutput.SendMessage($"#{indexStr} -> BcId: {gDoodad.ObjId} DoodadTemplateId: {gDoodad.TemplateId} - @DOODAD_NAME({gDoodad.TemplateId}) FuncGroupId {gDoodad.FuncGroupId}");
        }
        else if (go is Character gChar)
        {
            messageOutput.SendMessage($"#{indexStr} -> BcId: {gChar.ObjId} CharacterId: {gChar.Id} - {gChar.Name}");
        }
        else if (go is BaseUnit gBase)
        {
            messageOutput.SendMessage($"#{indexStr} -> BcId: {gBase.ObjId} - {gBase.Name}");
        }
        else
        {
            messageOutput.SendMessage($"#{indexStr} -> BcId: {go.ObjId}");
        }

        messageOutput.SendMessage($"#{indexStr} -> {go.Transform.ToFullString(true, true)}");

        // Cycle Children
        for (var i = 0; i < go.Transform.Children.Count; i++)
        {
            ShowObjectData(character, go.Transform.Children[i]?.GameObject, i, indexStr, messageOutput);
        }

        return 1 + go.Transform.Children.Count;
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 1)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var templateId = 0u;
        if (args.Length > 1 && !uint.TryParse(args[1], out templateId))
        {
            CommandManager.SendErrorText(this, messageOutput, "Error parsing template ID !");
            return;
        }

        if (templateId <= 0)
        {
            CommandManager.SendErrorText(this, messageOutput, "No template ID provided !");
            return;
        }

        var maxCount = 5u;
        if (args.Length > 2 && !uint.TryParse(args[2], out maxCount))
        {
            CommandManager.SendErrorText(this, messageOutput, "Error parsing max count !");
            return;
        }
        maxCount = Math.Clamp(maxCount, 1, 100);

        var sb = new StringBuilder();
        var results = new List<(float, GameObject)>();
        switch (args[0].ToLower())
        {
            case "doodads":
            case "doodad":
                var doodads = WorldManager.Instance.GetAllDoodads().Where(d => d.TemplateId == templateId).ToList();
                foreach (var gameObject in doodads)
                {
                    results.Add((character.GetDistanceTo(gameObject), gameObject));
                }
                break;

            case "mob":
            case "mobs":
            case "npc":
            case "npcs":
                var npcs = WorldManager.Instance.GetAllNpcs().Where(n => n.TemplateId == templateId).ToList();
                foreach (var gameObject in npcs)
                {
                    results.Add((character.GetDistanceTo(gameObject), gameObject));
                }
                break;

            case "gimmick":
            case "gimmicks":
                var gimmicks = WorldManager.Instance.GetAllNpcs().Where(n => n.TemplateId == templateId).ToList();
                foreach (var gameObject in gimmicks)
                {
                    results.Add((character.GetDistanceTo(gameObject), gameObject));
                }
                break;

            default:
                messageOutput.SendMessage($"[{CommandNames[0]}] unsupported type selected {args[0]}");
                break;
        }

        var sortedResults = results.OrderBy(go => go.Item1).ToList();
        var c = 0;

        if (sortedResults.Count > 0)
        {
            if (sortedResults.Count > maxCount)
            {
                messageOutput.SendMessage($"[{CommandNames[0]}] Showing {maxCount}/{sortedResults.Count} nearest object(s) of {args[0]}");
            }
            else
            {
                messageOutput.SendMessage($"[{CommandNames[0]}] Showing {sortedResults.Count} nearest object(s) of {args[0]}");
            }
            
            foreach (var gameObject in sortedResults)
            {
                ShowObjectData(character, gameObject.Item2, c, "", messageOutput);
                c++;
                if (c >= maxCount)
                    break;
            }
        }
        else
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] No results.");
        }
    }
}
