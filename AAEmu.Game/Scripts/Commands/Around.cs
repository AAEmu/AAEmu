using System;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.World.Transform;
using System.Numerics;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Scripts.Commands
{
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
        
        public int ShowObjectData(Character character, GameObject go, int index, string indexPrefix, bool verbose)
        {
            var indexStr = indexPrefix;
            if (indexStr != String.Empty)
                indexStr += " . ";
            indexStr += (index + 1).ToString();

            if (go is Doodad gDoodad)
                character.SendMessage( "#{0} -> BcId: {1} DoodadTemplateId: {2} - @DOODAD_NAME({2}) FuncGroupId {3}", 
                    indexStr, gDoodad.ObjId, gDoodad.TemplateId, gDoodad.FuncGroupId );
            else
            if (go is Character gChar)
                character.SendMessage("#{0} -> BcId: {1} CharacterId: {2} - {3}", 
                    indexStr, gChar.ObjId, gChar.Id, gChar.Name);
            else
            if (go is BaseUnit gBase)
                character.SendMessage("#{0} -> BcId: {1} - {2}", 
                    indexStr, gBase.ObjId, gBase.Name);
            else
                character.SendMessage("#{0} -> BcId: {1}", indexStr, go.ObjId.ToString());
            if (verbose)
            {
                // var shorts = go.Transform.World.ToRollPitchYawShorts();
                // var shortString = "(short[3])(r:" + shorts.Item1.ToString() + " p:" + shorts.Item2.ToString() + " y:" + shorts.Item3.ToString()+")";
                character.SendMessage("#{0} -> {1}",indexStr,go.Transform.ToFullString(true,true));
            }

            // Cycle Children
            for (var i = 0; i < go.Transform.Children.Count; i++)
                ShowObjectData(character, go.Transform.Children[i]?.GameObject, i, indexStr, verbose);
            return 1 + go.Transform.Children.Count;
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 1)
            {
                character.SendMessage("[Around] Using: " + CommandManager.CommandPrefix + "around " + GetCommandLineHelp());
                return;
            }

            float radius = 30f;
            if ((args.Length > 1) && (!float.TryParse(args[1], out radius)))
            {
                character.SendMessage("|cFFFF0000[Around] Error parsing Radius !|r");
                return;
            }

            var verbose = ((args.Length > 2) && (!string.IsNullOrWhiteSpace(args[2])));

            var sb = new StringBuilder();
            switch (args[0])
            {
                case "doodad":
                    var doodads = WorldManager.Instance.GetAround<Doodad>(character, radius);

                    character.SendMessage("[Around] Doodads:");
                    // sb.AppendLine("[Around] Doodads:");
                    for (var i = 0; i < doodads.Count; i++)
                    {
                        character.SendMessage("#" + (i + 1).ToString() + " -> BcId: " + doodads[i].ObjId.ToString() + " DoodadTemplateId: " + doodads[i].TemplateId.ToString() + " - @DOODAD_NAME(" + doodads[i].TemplateId.ToString() + ")" + ", FuncGroupId: " + doodads[i].FuncGroupId.ToString() );
                        // sb.AppendLine("#" + (i + 1).ToString() + " -> BcId: " + doodads[i].ObjId.ToString() + " DoodadTemplateId: " + doodads[i].TemplateId.ToString());
                        if (verbose)
                        {
                            var shorts = doodads[i].Transform.World.ToRollPitchYawShorts();
                            var shortString = "(short[3])(r:" + shorts.Item1.ToString() + " p:" + shorts.Item2.ToString() + " y:" + shorts.Item3.ToString()+")";
                            character.SendMessage("#{0} -> {1} = {2}\n",(i + 1).ToString(),doodads[i].Transform.ToString(),shortString);
                        }
                    }
                    character.SendMessage(sb.ToString());
                    character.SendMessage("[Around] Doodad count: {0}", doodads.Count);
                    break;

                case "mob":
                case "npc":
                    var npcs = WorldManager.Instance.GetAround<Npc>(character, radius);

                    character.SendMessage("[Around] NPCs");
                    // sb.AppendLine("[Around] NPCs");
                    for (var i = 0; i < npcs.Count; i++)
                    {
                        // TODO: Maybe calculate the localized name here ?
                        // string OriginalNPCName = NpcManager.Instance.GetTemplate(npcs[i].TemplateId).Name;
                        character.SendMessage("#" + (i + 1).ToString() + " -> BcId: " + npcs[i].ObjId.ToString() + " NpcTemplateId: " + npcs[i].TemplateId.ToString() + " - @NPC_NAME(" + npcs[i].TemplateId.ToString() + ")");
                        // sb.AppendLine("#" + (i + 1).ToString() + " -> BcId: " + npcs[i].ObjId.ToString() + " NpcTemplateId: " + npcs[i].TemplateId.ToString());
                        if (verbose)
                            character.SendMessage("#" + (i + 1).ToString() + " -> " + npcs[i].Transform.ToString()+"\n");
                    }

                    // character.SendMessage(sb.ToString());
                    character.SendMessage("[Around] NPC count: {0}", npcs.Count);
                    break;

                case "character":
                case "pc":
                case "player":
                    var characters = WorldManager.Instance.GetAround<Character>(character, radius);

                    character.SendMessage("[Around] Characters");
                    //sb.AppendLine("[Around] Characters");
                    for (var i = 0; i < characters.Count; i++)
                    {
                        character.SendMessage("#" + (i + 1).ToString() + " -> BcId: " + characters[i].ObjId.ToString() + " CharacterId: " + characters[i].Id.ToString() + " - " + characters[i].Name);
                        // sb.AppendLine("#" + (i + 1).ToString() + " -> BcId: " + characters[i].ObjId.ToString() + " CharacterId: " + characters[i].Id.ToString() + " - " + characters[i].Name);
                        //    sb.AppendLine($"#.{i + 1} -> BcId: {characters[i].ObjId} CharacterId: {characters[i].Id}");
                        if (verbose)
                            character.SendMessage("#" + (i + 1).ToString() + " -> " + characters[i].Transform.ToString()+"\n");
                    }
                    // character.SendMessage(sb.ToString());
                    character.SendMessage("[Around] Character count: {0}", characters.Count);
                    break;

                default:
                    var go = WorldManager.Instance.GetAround<GameObject>(character, radius);

                    var c = 0;
                    character.SendMessage("[Around] GameObjects:");
                    for (var i = 0; i < go.Count; i++)
                    {
                        if (go[i].Transform.Parent == null)
                            c += ShowObjectData(character, go[i], i, "", verbose);
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
                    character.SendMessage("[Around] Object Count: {0}", c);
                    break;
            }
            
        }

    }
}
