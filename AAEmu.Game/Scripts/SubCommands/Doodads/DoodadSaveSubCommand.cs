using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
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

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

public class DoodadSaveSubCommand : SubCommandBase
{
    //private static Dictionary<uint, Creature> _creatures;
    private bool _isSavingInProgress;
    private readonly object _saveLock = new();
    public DoodadSaveSubCommand()
    {
        Title = "[Doodad Save]";
        Description = "Save current state of a doodad to the doodads world file.";
        CallPrefix = $"{CommandManager.CommandPrefix}doodad save";
        AddParameter(new StringSubCommandParameter("target", "target", true, "all", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "Object Id", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        // Запускаем метод в отдельной задаче (нити)
        Task.Run(() =>
        {
            try
            {
                if (parameters.TryGetValue("ObjId", out var doodadObjId))
                    SaveById(character, doodadObjId, messageOutput);
                else
                    SaveAll(character, messageOutput);
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
            //_creatures = Creature.GetAllDoodads();
            var currentWorld = WorldManager.Instance.GetWorld(((Character)character).Transform.WorldId);
            var doodadsInWorld = WorldManager.Instance.GetAllDoodadsFromWorld(currentWorld.Id);
            var doodadSpawnersFromFile = LoadDoodadsFromFileByWorld(currentWorld);
            var doodadSpawnersToFile = new List<JsonDoodadSpawns>(doodadSpawnersFromFile);

            var addDoodads = doodadsInWorld.Where(n => n.Spawner?.Id == 0).ToList();
            var removeDoodads = doodadsInWorld.Where(n => n.Spawner?.Id == 0xffffffff).ToList();

            // Получаем все doodads и последний идентификатор объекта
            var allDoodads = WorldManager.Instance.GetAllDoodads();
            var lastObjId = allDoodads.Last().ObjId++;

            // Освобождаем память, присваивая null
            doodadsInWorld = null;
            allDoodads = null;
            GC.Collect(); // Вызываем сборку мусора

            // Создаем словарь для быстрого поиска элементов в doodadSpawnersToFile
            var doodadSpawnersDict = new Dictionary<(uint UnitId, float X, float Y, float Z), JsonDoodadSpawns>();
            var uniqueKeys = new HashSet<(uint, float, float, float)>();

            foreach (var spawns in doodadSpawnersToFile)
            {
                // Формируем ключ для проверки
                var key = (spawns.UnitId,
                    spawns.Position.X,
                    spawns.Position.Y,
                    spawns.Position.Z);

                // Проверяем уникальность ключа
                if (!uniqueKeys.Add(key))
                {
                    Logger.Warn($"Дубликат объекта с UnitId {spawns.UnitId} " + $"на позиции ({spawns.Position.X}, {spawns.Position.Y}, {spawns.Position.Z})");
                    continue;
                }

                // Если ключ уникален - добавляем в словарь
                doodadSpawnersDict.Add(key, spawns);
            }

            // Удаляем элементы из doodadSpawnersToFile, которые соответствуют removeDoodads
            Parallel.For(0, removeDoodads.Count, i =>
            {
                var doodad = removeDoodads[i];
                var key = (doodad.TemplateId, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y, doodad.Transform.World.Position.Z);
                if (!doodadSpawnersDict.TryGetValue(key, out var value))
                    return;

                lock (doodadSpawnersToFile)
                    doodadSpawnersToFile.Remove(value);
                doodadSpawnersDict.Remove(key);
            });

            // Добавляем элементы из addDoodads в doodadSpawnersToFile
            Parallel.For(0, addDoodads.Count, i =>
            {
                var doodad = addDoodads[i];
                var pos = doodad.Transform.World;
                var newDoodadSpawn = new JsonDoodadSpawns
                {
                    Id = lastObjId++,
                    UnitId = doodad.TemplateId,
                    //Title = GetSpawnName(doodad.TemplateId), // обновим Title
                    Position = new JsonPosition
                    {
                        X = pos.Position.X,
                        Y = pos.Position.Y,
                        Z = pos.Position.Z,
                        Roll = pos.Rotation.X.RadToDeg(),
                        Pitch = pos.Rotation.Y.RadToDeg(),
                        Yaw = pos.Rotation.Z.RadToDeg()
                    } //,
                    //Scale = doodad.Scale,
                    //FuncGroupId = doodad.FuncGroupId
                };

                lock (doodadSpawnersToFile)
                {
                    doodadSpawnersToFile.Add(newDoodadSpawn);
                }
            });

            var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", currentWorld.Name, $"doodad_spawns_all_{DateTime.Now:yyyyMMdd_HHmmss}.json.add");

            // Запись новых данных в файл
            var json = JsonConvert.SerializeObject(doodadSpawnersToFile, Formatting.Indented, new JsonModelsConverter());
            File.WriteAllText(jsonPathOut, json);

            stopwatch.Stop();

            // Вывод информации о количестве записей
            var initialCount = doodadSpawnersFromFile.Count;
            var removedCount = removeDoodads.Count;
            var addedCount = addDoodads.Count;
            var finalCount = doodadSpawnersToFile.Count;

            SendMessage(messageOutput, $"All doodads have been saved! Time taken: {stopwatch.ElapsedMilliseconds} ms\n" +
                                       $"Initial count: {initialCount}\n" +
                                       $"Removed count: {removedCount}\n" +
                                       $"Added count: {addedCount}\n" +
                                       $"Final count: {finalCount}");

            Logger.Warn($"All doodads have been saved! Time taken: {stopwatch.ElapsedMilliseconds} ms\n" +
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

    private void SaveById(ICharacter character, uint doodadObjId, IMessageOutput messageOutput)
    {
        //_creatures = Creature.GetAllDoodads();
        var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
        if (doodad is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Doodad with objId {doodadObjId} does not exist");
            Logger.Warn($"Doodad with objId {doodadObjId} does not exist");
            return;
        }

        var world = WorldManager.Instance.GetWorld(doodad.Transform.WorldId);
        if (world is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Could not find the worldId {doodad.Transform.WorldId}");
            Logger.Warn($"Could not find the worldId {doodad.Transform.WorldId}");
            return;
        }

        var spawn = new JsonDoodadSpawns
        {
            Id = doodad.Id,
            UnitId = doodad.TemplateId,
            //Title = GetSpawnName(doodad.TemplateId), // обновим Title
            Position = new JsonPosition
            {
                X = doodad.Transform.Local.Position.X,
                Y = doodad.Transform.Local.Position.Y,
                Z = doodad.Transform.Local.Position.Z,
                Roll = doodad.Transform.Local.Rotation.X.RadToDeg(),
                Pitch = doodad.Transform.Local.Rotation.Y.RadToDeg(),
                Yaw = doodad.Transform.Local.Rotation.Z.RadToDeg()
            } //,
            //Scale = doodad.Scale,
            //FuncGroupId = doodad.FuncGroupId
        };

        Dictionary<uint, JsonDoodadSpawns> spawnersFromFile = new();
        foreach (var spawnerFromFile in LoadDoodadsFromFileByWorld(world))
        {
            spawnersFromFile.TryAdd(spawnerFromFile.Id, spawnerFromFile);
        }

        spawnersFromFile[spawn.Id] = spawn;

        var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, $"doodad_spawns_add_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var json = JsonConvert.SerializeObject(spawnersFromFile.Values.ToArray(), Formatting.Indented, new JsonModelsConverter());
        File.WriteAllText(jsonPathOut, json);
        //SendDebugMessage(messageOutput, $"Doodad ObjId: {doodad.ObjId} has been saved!");
        SendMessage(messageOutput, $"All doodads have been saved with added doodad ObjId:{doodad.ObjId}, TemplateId:{doodad.TemplateId}");
        Logger.Warn($"All doodads have been saved with added doodad ObjId:{doodad.ObjId}, TemplateId:{doodad.TemplateId}");
    }

    private List<JsonDoodadSpawns> LoadDoodadsFromFileByWorld(World world)
    {
        var worldDirectory = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name);
        var jsonFiles = Directory.GetFiles(worldDirectory, "doodad_spawns*.json");

        if (jsonFiles.Length == 0)
        {
            Logger.Warn($"No doodad_spawns*.json files found in directory {worldDirectory}.");
            return [];
        }

        var allDoodads = new List<JsonDoodadSpawns>();

        foreach (var jsonFile in jsonFiles)
        {
            var contents = FileManager.GetFileContents(jsonFile);
            if (string.IsNullOrWhiteSpace(contents))
            {
                Logger.Warn($"File {jsonFile} is empty or could not be read.");
                continue;
            }

            Logger.Info($"Loading spawns from file {jsonFile} ...");

            var doodads = JsonHelper.DeserializeObject<List<JsonDoodadSpawns>>(contents);
            if (doodads != null)
            {
                allDoodads.AddRange(doodads);
            }
            else
            {
                Logger.Warn($"Failed to deserialize doodads from file {jsonFile}.");
            }
        }

        return allDoodads;
    }

    //private static string GetSpawnName(uint id)
    //{
    //    return _creatures.TryGetValue(id, out var creature) ? creature.Title : string.Empty;
    //}
}
