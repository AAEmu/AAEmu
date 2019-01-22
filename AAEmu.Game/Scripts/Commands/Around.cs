using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Scripts.Commands
{
    public class Around : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("around", this);
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 2)
            {
                character.SendMessage("[Around] Using: /around <objType> <radius>");
                character.SendMessage("[Around] ObjType: doodad, npc, character");
                return;
            }

            if (float.TryParse(args[1], out var radius))
            {
                var sb = new StringBuilder();
                switch (args[0])
                {
                    case "doodad":
                        var doodads = WorldManager.Instance.GetAround<Doodad>(character, radius);

                        sb.AppendLine("[Around] List");
                        for (var i = 0; i < doodads.Count; i++)
                            sb.AppendLine($"#.{i + 1} -> BcId: {doodads[i].ObjId} DoodadId: {doodads[i].TemplateId}");

                        character.SendMessage(sb.ToString());
                        character.SendMessage("[Around] Count: {0}", doodads.Count);
                        break;
                    case "npc":
                        var npcs = WorldManager.Instance.GetAround<Npc>(character, radius);

                        sb.AppendLine("[Around] List");
                        for (var i = 0; i < npcs.Count; i++)
                            sb.AppendLine($"#.{i + 1} -> BcId: {npcs[i].ObjId} NpcId: {npcs[i].TemplateId}");

                        character.SendMessage(sb.ToString());
                        character.SendMessage("[Around] Count: {0}", npcs.Count);
                        break;
                    case "character":
                        var characters = WorldManager.Instance.GetAround<Character>(character, radius);

                        sb.AppendLine("[Around] List");
                        for (var i = 0; i < characters.Count; i++)
                            sb.AppendLine($"#.{i + 1} -> BcId: {characters[i].ObjId} CharacterId: {characters[i].Id}");

                        character.SendMessage(sb.ToString());
                        character.SendMessage("[Around] Count: {0}", characters.Count);
                        break;
                }
            }
            else
                character.SendMessage("[Around] Throw parse radius value!");
        }
    }
}