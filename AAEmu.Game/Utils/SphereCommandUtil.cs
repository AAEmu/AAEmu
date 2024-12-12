using System.Collections.Generic;
using System.IO;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils.Scripts;
using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Utils;

public class SphereCommandUtil
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public static void GetCommandChoice(ICommand command, IMessageOutput messageOutput, Character character, string choice, string[] args)
    {
        uint questId;

        switch (choice)
        {
            case "quest":
                if (args.Length > 1)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        GetQuestSpheres(character, questId);
                    }
                }
                else
                {
                    CommandManager.SendErrorText(command, messageOutput, "/sphere quest <questId>");
                }
                break;
            case "add":
                if (args.Length >= 4)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        if (uint.TryParse(args[2], out var sphereId))
                        {
                            if (float.TryParse(args[3], out var radius))
                            {
                                AddQuestSphere(character, questId, sphereId, radius);
                            }
                        }
                    }
                }
                else
                {
                    CommandManager.SendErrorText(command, messageOutput, "/sphere add <questId> <sphereId> <radius>");
                    CommandManager.SendErrorText(command, messageOutput, "Adding a sphere uses character's current position!");
                }
                break;
            case "list":
                if (args.Length > 1)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        GetSphereList(character, questId);
                    }
                }
                else
                {
                    CommandManager.SendErrorText(command, messageOutput, "/sphere list <questId>");
                }
                break;
            case "remove":
                if (args.Length > 1)
                {
                    if (uint.TryParse(args[1], out var jsonId))
                    {
                        RemoveQuestSphere(character, jsonId);
                    }
                }
                else
                {
                    CommandManager.SendErrorText(command, messageOutput, "/sphere remove <jsonId>");
                }
                break;
            case "goto":
                if (args.Length > 1)
                {
                    if (uint.TryParse(args[1], out var jsonId))
                    {
                        GotoSphere(character, jsonId);
                    }
                }
                else
                {
                    CommandManager.SendErrorText(command, messageOutput, "/sphere goto <jsonId>");
                }
                break;
            default:
                CommandManager.SendDefaultHelpText(command, messageOutput);
                break;
        }
    }

    private static void GetQuestSpheres(Character character, uint questId)
    {
        var sphereIds = SphereGameData.GetQuestSphere(questId);
        if (sphereIds != null)
        {
            foreach (var x in sphereIds)
            {
                character.SendMessage($"Found SphereId: {x} for QuestId: {questId}");
            }
        }
        else
        {
            character.SendMessage($"No Spheres required for QuestId: {questId}");
        }
    }

    private static void AddQuestSphere(Character character, uint questId, uint sphereId, float radius)
    {
        var worlds = WorldManager.Instance.GetWorlds();

        foreach (var world in worlds)
        {
            if (character.Transform.WorldId != world.Id)
            {
                continue;
            }

            var path = ($"{FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json");

            var contents =
                FileManager.GetFileContents(
                    $"{FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json");
            if (string.IsNullOrWhiteSpace(contents))
                Logger.Warn(
                    $"File {FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json doesn't exists or is empty.");
            else
            {
                if (JsonHelper.TryDeserializeObject(contents, out List<JsonQuestSphere> spheres, out _))
                {
                    uint uuid = 0;
                    if (spheres.Count > 0)
                    {
                        uuid = ((spheres[spheres.Count - 1].Id) + 1);
                    }
                    else
                    {
                        uuid = (uint)(spheres.Count + 1);
                    }

                    var pos = new JsonPosition()
                    {
                        X = character.Transform.Local.Position.X,
                        Y = character.Transform.Local.Position.Y,
                        Z = character.Transform.Local.Position.Z,
                    };

                    var newEntry = new JsonQuestSphere
                    {
                        Id = uuid,
                        QuestId = questId,
                        SphereId = sphereId,
                        Radius = radius,
                        Position = pos
                    };

                    spheres.Add(newEntry);

                }
                else
                    throw new GameException(
                        $"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json file");

                var json = JsonConvert.SerializeObject(spheres.ToArray(), Formatting.Indented);
                File.WriteAllText(path, json);

                character.SendMessage("Sphere successfully added!");
            }
        }
    }

    private static void GetSphereList(Character character, uint questId)
    {
        var triggers = SphereQuestManager.Instance.GetSphereQuestTriggers();
        var count = 0;
        foreach (var questTrigger in triggers)
        {
            if (questTrigger.Owner.Id != character.Id)
                continue;
            if ((questId > 0) && (questTrigger.Quest.Id != questId))
                continue;
            var currentDistance = MathUtil.CalculateDistance(character.Transform.World.Position, questTrigger.Sphere.Xyz, true);
            character.SendMessage($"[Sphere] Quest {questTrigger.Quest.TemplateId} - Component: {questTrigger.Sphere.ComponentId} - TriggerSphere  xyz:{questTrigger.Sphere.Xyz} , radius:{questTrigger.Sphere.Radius}m, at {currentDistance:F1}m away");
            count++;
        }
        if (count <= 0)
            character.SendMessage($"[Sphere] No active sphere triggers found" + (questId > 0 ? $" for QuestId {questId}" : ""));
    }

    public static void RemoveQuestSphere(Character character, uint jsonId)
    {
        bool found = false;

        var worlds = WorldManager.Instance.GetWorlds();

        foreach (var world in worlds)
        {
            if (character.Transform.WorldId != world.Id)
            {
                continue;
            }

            var path = ($"{FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json");

            var contents =
                FileManager.GetFileContents(
                    $"{FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json");
            if (string.IsNullOrWhiteSpace(contents))
                Logger.Warn(
                    $"File {FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json doesn't exists or is empty.");
            else
            {
                if (JsonHelper.TryDeserializeObject(contents, out List<JsonQuestSphere> spheres, out _))
                {

                    var index = 0;
                    foreach (var sphere in spheres)
                    {
                        if (sphere.Id == jsonId)
                        {
                            found = true;
                            spheres.RemoveAt(index);
                            break;
                        }

                        index++;

                    }

                    if (!found)
                    {
                        character.SendMessage($"Json entry with ID {jsonId} does not exist!");
                    }
                }
                else
                    throw new GameException(
                        $"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json file");

                var json = JsonConvert.SerializeObject(spheres.ToArray(), Formatting.Indented);
                File.WriteAllText(path, json);

                if (found)
                    character.SendMessage($"Removed Json entry with ID {jsonId} successfully");
            }
        }
    }

    public static void GotoSphere(Character character, uint jsonId)
    {
        var worlds = WorldManager.Instance.GetWorlds();

        foreach (var world in worlds)
        {
            if (character.Transform.WorldId != world.Id)
            {
                continue;
            }

            var path = ($"{FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json");

            var contents =
                FileManager.GetFileContents(
                    $"{FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json");
            if (string.IsNullOrWhiteSpace(contents))
                Logger.Warn(
                    $"File {FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json doesn't exists or is empty.");
            else
            {
                if (JsonHelper.TryDeserializeObject(contents, out List<JsonQuestSphere> spheres, out _))
                {
                    bool found = false;
                    foreach (var sphere in spheres)
                    {
                        if (sphere.Id == jsonId)
                        {
                            found = true;
                            character.SendPacket(new SCTeleportUnitPacket(0, 0, sphere.Position.X, sphere.Position.Y, sphere.Position.Z, 0));
                            break;
                        }
                    }

                    if (!found)
                    {
                        character.SendMessage($"Json entry with ID {jsonId} does not exist!");
                    }
                }
                else
                    throw new GameException(
                        $"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/quest_sphere.json file");
            }
        }
    }
}
