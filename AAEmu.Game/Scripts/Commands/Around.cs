using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Core.Managers.UnitManagers;

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
            return "<doodad||npc||player> [radius]";
        }

        public string GetCommandHelpText()
        {
            return "Creates a list of specified <objectType> in a [radius] radius around you. Default radius is 30.\n" +
                "Note: Only lists objects in viewing range of you (recommanded maximum radius of 100).";
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

            var sb = new StringBuilder();
            switch (args[0])
            {
                case "doodad":
                    var doodads = WorldManager.Instance.GetAround<Doodad>(character, radius);

                    character.SendMessage("[Around] Doodads:");
                    // sb.AppendLine("[Around] Doodads:");
                    for (var i = 0; i < doodads.Count; i++)
                    {
                        character.SendMessage("#" + (i + 1).ToString() + " -> BcId: " + doodads[i].ObjId.ToString() + " DoodadTemplateId: " + doodads[i].TemplateId.ToString() + " - @DOODAD_NAME(" + doodads[i].TemplateId.ToString() + ")");
                        // sb.AppendLine("#" + (i + 1).ToString() + " -> BcId: " + doodads[i].ObjId.ToString() + " DoodadTemplateId: " + doodads[i].TemplateId.ToString());
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
                    }
                    // character.SendMessage(sb.ToString());
                    character.SendMessage("[Around] Character count: {0}", characters.Count);
                    break;

                default:
                    character.SendMessage("|cFFFF0000[Around] Unknown object type {0} !|r",args[0]);
                    break;
            }
            
        }
    }
}
