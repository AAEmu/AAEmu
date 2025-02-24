using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
//using AAEmu.Commons.Utils.Creatures;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Converters;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

using Newtonsoft.Json;

namespace AAEmu.Game.Scripts.SubCommands.Npcs;

public class NpcSaveSubCommand : SubCommandBase
{
    //private static Dictionary<uint, Creature> _creatures;
    private bool _isSavingInProgress;
    private readonly object _saveLock = new();

    public NpcSaveSubCommand()
    {
        Title = "[Npc Save]";
        Description = "Save one or all npc positions in the current character world to the world npc spawns file";
        CallPrefix = $"{CommandManager.CommandPrefix}save";
        AddParameter(new StringSubCommandParameter("target", "target", true, "all", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object Id", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        // Запускаем метод в отдельной задаче (нити)
        Task.Run(() =>
        {
            try
            {
                if (parameters.TryGetValue("ObjId", out var npcObjId))
                {
                    SaveById(character, npcObjId, messageOutput);
                }
                else
                {
                    SaveAll(character, messageOutput);
                }
            }
            catch (Exception ex)
            {
                // Обработка исключения, например, запись в лог
                Logger.Error($"Ошибка при выполнении метода: {ex.Message}");
            }
        });
    }

    private void SaveAll(ICharacter character, IMessageOutput messageOutput)
    {
        // Проверка на выполнение записи
        if (_isSavingInProgress)
        {
            SendMessage(messageOutput, "Save operation is already in progress.");
            return;
        }

        lock (_saveLock)
        {
            if (_isSavingInProgress)
            {
                SendMessage(messageOutput, "Save operation is already in progress.");
                return;
            }

            _isSavingInProgress = true;
        }

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            //_creatures = Creature.GetAllCreatures();
            var currentWorld = WorldManager.Instance.GetWorld(((Character)character).Transform.WorldId);
            var npcsInWorld = WorldManager.Instance.GetAllNpcsFromWorld(currentWorld.Id);
            var npcSpawnersFromFile = LoadNpcsFromFileByWorld(currentWorld);
            var npcSpawnersToFile = npcSpawnersFromFile.ToList();

            var addNpcs = npcsInWorld.Where(n => n.Spawner?.Id == 0).ToList();
            var removeNpcs = npcsInWorld.Where(n => n.Spawner?.Id == 0xffffffff).ToList();

            // Получаем все npcs и последний идентификатор объекта
            var allNpcs = WorldManager.Instance.GetAllNpcs();
            var lastObjId = allNpcs.Last().ObjId++;

            // Освобождаем память, присваивая null
            npcsInWorld = null;
            allNpcs = null;
            GC.Collect(); // Вызываем сборку мусора

            // Создаем словарь для быстрого поиска элементов в npcSpawnersToFile
            var npcSpawnersDict = new Dictionary<(uint UnitId, float X, float Y), JsonNpcSpawns>();
            var uniqueKeys = new HashSet<(uint, float, float)>();

            foreach (var spawns in npcSpawnersToFile)
            {
                // Формируем ключ для проверки
                var key = (spawns.UnitId,
                    spawns.Position.X,
                    spawns.Position.Y);

                // Проверяем уникальность ключа
                if (!uniqueKeys.Add(key))
                {
                    Logger.Warn($"Дубликат объекта с UnitId {spawns.UnitId} " + $"на позиции ({spawns.Position.X}, {spawns.Position.Y}, {spawns.Position.Z})");
                    continue;
                }

                // Если ключ уникален - добавляем в словарь
                npcSpawnersDict.Add(key, spawns);
            }

            // Удаляем элементы из npcSpawnersToFile, которые соответствуют removeNpcs
            Parallel.For(0, removeNpcs.Count, i =>
            {
                var npc = removeNpcs[i];
                var key = (npc.TemplateId, npc.Transform.World.Position.X, npc.Transform.World.Position.Y);
                if (!npcSpawnersDict.TryGetValue(key, out var value))
                    return;

                lock (npcSpawnersToFile)
                    npcSpawnersToFile.Remove(value);
                npcSpawnersDict.Remove(key);
            });

            // Добавляем элементы из addNpcs в npcSpawnersToFile
            Parallel.For(0, addNpcs.Count, i =>
            {
                var npc = addNpcs[i];
                var pos = npc.Transform.World;
                var newNpcSpawn = new JsonNpcSpawns
                {
                    Id = lastObjId++,
                    UnitId = npc.TemplateId,
                    //Title = GetSpawnName(npc.TemplateId), // обновим Title
                    Position = new JsonPosition
                    {
                        X = pos.Position.X,
                        Y = pos.Position.Y,
                        Z = pos.Position.Z,
                        Roll = pos.Rotation.X.RadToDeg(),
                        Pitch = pos.Rotation.Y.RadToDeg(),
                        Yaw = pos.Rotation.Z.RadToDeg()
                    } //,
                    //Scale = npc.Scale,
                };

                lock (npcSpawnersToFile)
                {
                    npcSpawnersToFile.Add(newNpcSpawn);
                }
            });

            var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", currentWorld.Name, $"npc_spawns_all_{DateTime.Now:yyyyMMdd_HHmmss}.json.add");

            // Запись новых данных в файл
            var json = JsonConvert.SerializeObject(npcSpawnersToFile, Formatting.Indented, new JsonModelsConverter());
            File.WriteAllText(jsonPathOut, json);

            stopwatch.Stop();

            // Вывод информации о количестве записей
            var initialCount = npcSpawnersFromFile.Count;
            var removedCount = removeNpcs.Count;
            var addedCount = addNpcs.Count;
            var finalCount = npcSpawnersToFile.Count;

            SendMessage(messageOutput, $"All npcs have been saved! Time taken: {stopwatch.ElapsedMilliseconds} ms\n" +
                                       $"Initial count: {initialCount}\n" +
                                       $"Removed count: {removedCount}\n" +
                                       $"Added count: {addedCount}\n" +
                                       $"Final count: {finalCount}");

            Logger.Warn($"All npcs have been saved! Time taken: {stopwatch.ElapsedMilliseconds} ms\n" +
                        $"Initial count: {initialCount}\n" +
                        $"Removed count: {removedCount}\n" +
                        $"Added count: {addedCount}\n" +
                        $"Final count: {finalCount}");
        }
        finally
        {
            lock (_saveLock)
            {
                _isSavingInProgress = false;
            }
        }
    }

    private void SaveById(ICharacter character, uint npcObjId, IMessageOutput messageOutput)
    {
        //_creatures = Creature.GetAllCreatures();
        //var spawners = new List<JsonNpcSpawns>();
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Npc with objId {npcObjId} Does not exist");
            Logger.Info($"Npc with objId {npcObjId} Does not exist");
            return;
        }

        var world = WorldManager.Instance.GetWorld(npc.Transform.WorldId);
        if (world is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Could not find the worldId {npc.Transform.WorldId}");
            Logger.Info($"Could not find the worldId {npc.Transform.WorldId}");
            return;
        }

        var spawn = new JsonNpcSpawns
        {
            Id = npc.ObjId,
            UnitId = npc.TemplateId,
            //Title = GetSpawnName(npc.TemplateId), // обновим Title
            Position = new JsonPosition
            {
                X = npc.Transform.Local.Position.X,
                Y = npc.Transform.Local.Position.Y,
                Z = npc.Transform.Local.Position.Z,
                Roll = npc.Transform.Local.Rotation.X.RadToDeg(),
                Pitch = npc.Transform.Local.Rotation.Y.RadToDeg(),
                Yaw = npc.Transform.Local.Rotation.Z.RadToDeg()
            } //,
            //Scale = npc.Scale
        };

        Dictionary<uint, JsonNpcSpawns> spawnersFromFile = new();
        foreach (var spawnerFromFile in LoadNpcsFromFileByWorld(world))
        {
            spawnersFromFile.TryAdd(spawnerFromFile.Id, spawnerFromFile);
        }

        spawnersFromFile[spawn.Id] = spawn;

        var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "npc_spawns_new.json");
        var json = JsonConvert.SerializeObject(spawnersFromFile.Values.ToArray(), Formatting.Indented, new JsonModelsConverter());
        File.WriteAllText(jsonPathOut, json);
        SendMessage(messageOutput, $"All npcs have been saved with added npc ObjId:{npc.ObjId}, TemplateId:{npc.TemplateId}");
        Logger.Info($"All npcs have been saved with added npc ObjId:{npc.ObjId}, TemplateId:{npc.TemplateId}");
    }

    private List<JsonNpcSpawns> LoadNpcsFromFileByWorld(World world)
    {
        var worldDirectory = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name);
        var jsonFiles = Directory.GetFiles(worldDirectory, "npc_spawns*.json");

        if (jsonFiles.Length == 0)
        {
            Logger.Warn($"No npc_spawns*.json files found in directory {worldDirectory}.");
            return [];
        }

        var allNpcs = new List<JsonNpcSpawns>();

        foreach (var jsonFile in jsonFiles)
        {
            var contents = FileManager.GetFileContents(jsonFile);
            if (string.IsNullOrWhiteSpace(contents))
            {
                Logger.Warn($"File {jsonFile} is empty or could not be read.");
                continue;
            }

            Logger.Info($"Loading spawns from file {jsonFile} ...");

            var npcs = JsonHelper.DeserializeObject<List<JsonNpcSpawns>>(contents);
            if (npcs != null)
            {
                allNpcs.AddRange(npcs);
            }
            else
            {
                Logger.Warn($"Failed to deserialize npcs from file {jsonFile}.");
            }
        }

        return allNpcs;
    }

    //private static string GetSpawnName(uint id)
    //{
    //    return _creatures.TryGetValue(id, out var creature) ? creature.Title : string.Empty;
    //}
}
