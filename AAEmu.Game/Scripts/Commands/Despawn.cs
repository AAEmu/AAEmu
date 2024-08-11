using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Despawn : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "despawn" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<npc || doodad> < <objId> || <templateId> <radius> >";
    }

    public string GetCommandHelpText()
    {
        return "Despawns a npc or doodad using by <objId> or <templateId> inside a <radius> range.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var action = args[0];
        var radius = 30f;
        var useRadius = args.Length >= 3;
        if (!uint.TryParse(args[1], out var unitId))
        {
            if (useRadius)
            {
                CommandManager.SendErrorText(this, messageOutput, "<templateId> parse error");
            }
            else
            {
                CommandManager.SendErrorText(this, messageOutput, "<objId> parse error");
            }

            return;
        }

        if (args.Length >= 3 && !float.TryParse(args[2], out radius))
        {
            CommandManager.SendErrorText(this, messageOutput, "<radius> parse error");
            return;
        }

        if (!useRadius)
        {
            switch (action)
            {
                case "doodad":
                    var myDoodad = WorldManager.Instance.GetDoodad(unitId);

                    if (myDoodad != null)
                    {
                        CommandManager.SendNormalText(this, messageOutput,
                            $"Removing Doodad with ID {myDoodad.ObjId} - @DOODAD_NAME({myDoodad.TemplateId})");
                        ObjectIdManager.Instance.ReleaseId(myDoodad.ObjId);
                        myDoodad.Delete();
                    }
                    else
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"Doodad with Id {unitId} does not exist");
                    }

                    break;
                case "npc":
                    var myNPC = WorldManager.Instance.GetNpc(unitId);

                    if (myNPC != null)
                    {
                        CommandManager.SendNormalText(this, messageOutput,
                            $"Removing NPC with ID {myNPC.ObjId} - @NPC_NAME({myNPC.TemplateId})");
                        ObjectIdManager.Instance.ReleaseId(myNPC.ObjId);
                        myNPC.Delete();
                    }
                    else
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"NPC with objectId {unitId} does not exist");
                    }

                    break;
                case "unit":
                    var myUnit = WorldManager.Instance.GetBaseUnit(unitId);

                    if (myUnit != null)
                    {
                        CommandManager.SendNormalText(this, messageOutput, $"Removing Transfer with ID {myUnit.ObjId}");
                        ObjectIdManager.Instance.ReleaseId(myUnit.ObjId);
                        myUnit.Delete();
                    }
                    else
                    {
                        CommandManager.SendErrorText(this, messageOutput,
                            $"Unit with objectId {unitId} does not exist");
                    }

                    break;
                default:
                    CommandManager.SendErrorText(this, messageOutput, $"Unknown action {action}");
                    break;
            }
        }
        else
        {
            var removedCount = 0;
            // Use radius
            switch (action)
            {
                case "doodad":
                    var myDoodads = WorldManager.GetAround<Doodad>(character, radius);

                    foreach (var doodad in myDoodads)
                    {
                        if (doodad.TemplateId == unitId)
                        {
                            ObjectIdManager.Instance.ReleaseId(doodad.ObjId);
                            doodad.Delete();
                            removedCount++;
                        }
                    }

                    CommandManager.SendNormalText(this, messageOutput,
                        $"Removed {removedCount} Doodad(s) with TemplateID {unitId} - @DOODAD_NAME({unitId})");
                    break;
                case "npc":
                    var myNPCs = WorldManager.GetAround<Npc>(character, radius);

                    foreach (var npc in myNPCs)
                    {
                        if (npc.TemplateId == unitId)
                        {
                            ObjectIdManager.Instance.ReleaseId(npc.ObjId);
                            npc.Delete();
                            removedCount++;
                        }
                    }

                    CommandManager.SendNormalText(this, messageOutput,
                        $"Removed {removedCount} NPC(s) with TemplateID {unitId} - @NPC_NAME({unitId})");
                    break;
                default:
                    CommandManager.SendErrorText(this, messageOutput, $"Unknown action {action}");
                    break;
            }
        }
    }
}
