using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Json;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using Newtonsoft.Json;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.Commands;

public class Nwrite : ICommand
{
    public string[] CommandNames { get; set; } = ["nwrite", "nw"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) [doodad <objectId>]";
    }

    public string GetCommandHelpText()
    {
        return "Writes the targeted or given Doodad's current position and rotation to doodad_spawns.json";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var worlds = WorldManager.Instance.GetWorlds().ToList();

        Doodad doodad = null;

        // Doodad by Id ?
        if (args.Length >= 2 && args[0].Equals("doodad", StringComparison.CurrentCultureIgnoreCase) &&
            uint.TryParse(args[1], out var targetDoodadId))
        {
            doodad = character.ParentWorld.GetDoodad(targetDoodadId);
        }
        else if (character.CurrentTarget is Doodad targetedDoodad)
        {
            doodad = targetedDoodad;
        }

        if (doodad == null)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (doodad != null)
        {
            // Save target Doodad
            try
            {
                // TODO: replace with templates instead of instances
                foreach (var world in worlds)
                {
                    var jsonPath = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Template.Name, "doodad_spawns.json");
                    if (doodad.Spawner.Position.WorldId == world.Id)
                    {
                        var contents = FileManager.GetFileContents(jsonPath);
                        if (string.IsNullOrWhiteSpace(contents))
                        {
                            CommandManager.SendErrorText(this, messageOutput,
                                $"File {jsonPath} doesn't exists or is empty.");
                        }
                        else
                        {
                            if (!JsonHelper.TryDeserializeObject(contents, out List<JsonDoodadSpawns> spawners, out _))
                            {
                                continue;
                            }

                            if (doodad.Spawner.Id == 0) // spawned into the game manually
                            {
                                var newId = spawners[^1].Id + 1;
                                var pos = new JsonPosition
                                {
                                    X = doodad.Transform.World.Position.X,
                                    Y = doodad.Transform.World.Position.Y,
                                    Z = doodad.Transform.World.Position.Z,
                                    Roll =
                                        (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.X.RadToDeg()),
                                    Pitch =
                                        (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.Y.RadToDeg()),
                                    Yaw = (float)MathUtil.ClampDegAngle(
                                        doodad.Transform.Local.Rotation.Z.RadToDeg())
                                };

                                var newEntry = new JsonDoodadSpawns
                                {
                                    Id = newId,
                                    UnitId = doodad.TemplateId,
                                    Position = pos
                                };
                                spawners.Add(newEntry);

                                doodad.Spawner.Id = newId; //Set ID in case you edit it after adding!
                            }
                            else
                            {
                                foreach (var spawner in spawners)
                                {
                                    if (spawner.Id == doodad.Spawner.Id)
                                    {
                                        spawner.Position.X = doodad.Transform.World.Position.X;
                                        spawner.Position.Y = doodad.Transform.World.Position.Y;
                                        spawner.Position.Z = doodad.Transform.World.Position.Z;
                                        spawner.Position.Roll =
                                            (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.X.RadToDeg());
                                        spawner.Position.Pitch =
                                            (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.Y.RadToDeg());
                                        spawner.Position.Yaw =
                                            (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.Z.RadToDeg());
                                        break;
                                    }
                                }
                            }

                            var json = JsonConvert.SerializeObject(spawners.ToArray(), Formatting.Indented);
                            File.WriteAllText(jsonPath, json);
                            CommandManager.SendNormalText(this, messageOutput,
                                $"Doodad ObjId: {doodad.ObjId} has been saved!");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                CommandManager.SendErrorText(this, messageOutput, $"Exception: {e.Message}");
            }
        }
        else
        {
            character.SendMessage("[Nwrite] I don't know what to do here");
        }
    }
}
