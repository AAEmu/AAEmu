using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Around : ICommand
{
    public void OnLoad()
    {
        string[] name = { "around", "near" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "<doodad||npc||player> [radius] [verbose]";
    }

    public string GetCommandHelpText()
    {
        return "Creates a list of specified <objectType> in a [radius] radius around you. Default radius is 30.\n" +
            "Note: Only lists objects in viewing range of you (recommended maximum radius of 100).";
    }

    public static int ShowObjectData(Character character, GameObject go, int index, string indexPrefix, bool verbose, IMessageOutput messageOutput)
    {
        var indexStr = indexPrefix;
        if (indexStr != string.Empty)
            indexStr += " . ";
        indexStr += (index + 1).ToString();

        if (go is Doodad gDoodad)
            messageOutput.SendMessage($"#{indexStr} -> BcId: {gDoodad.ObjId} DoodadTemplateId: {gDoodad.TemplateId} - @DOODAD_NAME({gDoodad.TemplateId}) FuncGroupId {gDoodad.FuncGroupId}");
        else
        if (go is Character gChar)
            messageOutput.SendMessage($"#{indexStr} -> BcId: {gChar.ObjId} CharacterId: {gChar.Id} - {gChar.Name}");
        else
        if (go is BaseUnit gBase)
            messageOutput.SendMessage($"#{indexStr} -> BcId: {gBase.ObjId} - {gBase.Name}");
        else
            messageOutput.SendMessage($"#{indexStr} -> BcId: {go.ObjId}");
        if (verbose)
        {
            // var shorts = go.Transform.World.ToRollPitchYawShorts();
            // var shortString = "(short[3])(r:" + shorts.Item1.ToString() + " p:" + shorts.Item2.ToString() + " y:" + shorts.Item3.ToString()+")";
            messageOutput.SendMessage($"#{indexStr} -> {go.Transform.ToFullString(true, true)}");
        }

        // Cycle Children
        for (var i = 0; i < go.Transform.Children.Count; i++)
            ShowObjectData(character, go.Transform.Children[i]?.GameObject, i, indexStr, verbose, messageOutput);

        return 1 + go.Transform.Children.Count;
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 1)
        {
            messageOutput.SendMessage("[Around] Using: " + CommandManager.CommandPrefix + "around " + GetCommandLineHelp());
            return;
        }

        float radius = 30f;
        if ((args.Length > 1) && (!float.TryParse(args[1], out radius)))
        {
            messageOutput.SendMessage("|cFFFF0000[Around] Error parsing Radius !|r");
            return;
        }

        var verbose = ((args.Length > 2) && (!string.IsNullOrWhiteSpace(args[2])));

        var sb = new StringBuilder();
        switch (args[0])
        {
            case "doodad":
                var doodads = WorldManager.GetAround<Doodad>(character, radius);

                messageOutput.SendMessage("[Around] Doodads:");
                // sb.AppendLine("[Around] Doodads:");
                for (var i = 0; i < doodads.Count; i++)
                {
                    messageOutput.SendMessage("#" + (i + 1).ToString() + " -> BcId: " + doodads[i].ObjId.ToString() + " DoodadTemplateId: " + doodads[i].TemplateId.ToString() + " - @DOODAD_NAME(" + doodads[i].TemplateId.ToString() + ")" + ", FuncGroupId: " + doodads[i].FuncGroupId.ToString());

                    messageOutput.SendMessage($"#{(i + 1)} -> SpawnerID = {(doodads[i].Spawner?.Id.ToString() ?? "none")}, Respawns Template: {(doodads[i].Spawner?.RespawnDoodadTemplateId.ToString() ?? "default")}\n");

                    // sb.AppendLine("#" + (i + 1).ToString() + " -> BcId: " + doodads[i].ObjId.ToString() + " DoodadTemplateId: " + doodads[i].TemplateId.ToString());
                    if (verbose)
                    {
                        var shorts = doodads[i].Transform.World.ToRollPitchYawShorts();
                        var shortString = "(short[3])(r:" + shorts.Item1.ToString() + " p:" + shorts.Item2.ToString() + " y:" + shorts.Item3.ToString() + ")";
                        messageOutput.SendMessage($"#{(i + 1)} -> {doodads[i].Transform} = {shortString}\n");
                    }

                }
                messageOutput.SendMessage(sb.ToString());
                messageOutput.SendMessage($"[Around] Doodad count: {doodads.Count}");
                break;

            case "mob":
            case "npc":
                var npcs = WorldManager.GetAround<Npc>(character, radius);

                messageOutput.SendMessage("[Around] NPCs");
                // sb.AppendLine("[Around] NPCs");
                for (var i = 0; i < npcs.Count; i++)
                {
                    // TODO: Maybe calculate the localized name here ?
                    // string OriginalNPCName = NpcManager.Instance.GetTemplate(npcs[i].TemplateId).Name;
                    messageOutput.SendMessage("#" + (i + 1).ToString() + " -> BcId: " + npcs[i].ObjId.ToString() + " NpcTemplateId: " + npcs[i].TemplateId.ToString() + " - @NPC_NAME(" + npcs[i].TemplateId.ToString() + ")");
                    // sb.AppendLine("#" + (i + 1).ToString() + " -> BcId: " + npcs[i].ObjId.ToString() + " NpcTemplateId: " + npcs[i].TemplateId.ToString());
                    if (verbose)
                        messageOutput.SendMessage("#" + (i + 1).ToString() + " -> " + npcs[i].Transform.ToString() + "\n");
                }

                // character.SendMessage(sb.ToString());
                messageOutput.SendMessage($"[Around] NPC count: {npcs.Count}");
                break;

            case "character":
            case "pc":
            case "player":
                var characters = WorldManager.GetAround<Character>(character, radius);

                messageOutput.SendMessage("[Around] Characters");
                //sb.AppendLine("[Around] Characters");
                for (var i = 0; i < characters.Count; i++)
                {
                    messageOutput.SendMessage("#" + (i + 1).ToString() + " -> BcId: " + characters[i].ObjId.ToString() + " CharacterId: " + characters[i].Id.ToString() + " - " + characters[i].Name);
                    // sb.AppendLine("#" + (i + 1).ToString() + " -> BcId: " + characters[i].ObjId.ToString() + " CharacterId: " + characters[i].Id.ToString() + " - " + characters[i].Name);
                    //    sb.AppendLine($"#.{i + 1} -> BcId: {characters[i].ObjId} CharacterId: {characters[i].Id}");
                    if (verbose)
                        messageOutput.SendMessage("#" + (i + 1).ToString() + " -> " + characters[i].Transform.ToString() + "\n");
                }
                // character.SendMessage(sb.ToString());
                messageOutput.SendMessage($"[Around] Character count: {characters.Count}");
                break;

            default:
                var go = WorldManager.GetAround<GameObject>(character, radius);

                var c = 0;
                messageOutput.SendMessage("[Around] GameObjects:");
                for (var i = 0; i < go.Count; i++)
                {
                    if (go[i].Transform.Parent == null)
                        c += ShowObjectData(character, go[i], i, "", verbose, messageOutput);
                    /*
                    character.SendMessage("#" + (i + 1).ToString() + " -> BcId: " + go[i].ObjId.ToString());
                    if (verbose)
                    {
                        var shorts = go[i].Transform.World.ToRollPitchYawShorts();
                        var shortString = "(short[3])(r:" + shorts.Item1.ToString() + " p:" + shorts.Item2.ToString() + " y:" + shorts.Item3.ToString()+")";
                        character.SendMessage("#{0} -> {1} = {2}\n",(i + 1).ToString(),go[i].Transform.ToString(),shortString);
                    }
                    */
                }
                messageOutput.SendMessage($"[Around] Object Count: {c}");
                break;
        }

    }

}
