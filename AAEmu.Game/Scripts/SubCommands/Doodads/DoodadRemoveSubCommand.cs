using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

public class DoodadRemoveSubCommand : SubCommandBase
{
    public DoodadRemoveSubCommand()
    {
        Title = "[Doodad Remove]";
        Description = "Remove using an Doodad <ObjId> or using ObjId = 0 for the nearest Doodad";
        CallPrefix = $"{CommandManager.CommandPrefix}remove";
        AddParameter(new StringSubCommandParameter("target", "target", true, "all", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object id", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        // Запускаем метод в отдельной задаче (нити)
        Task.Run(() =>
        {
            try
            {
                if (parameters.TryGetValue("ObjId", out var doodadObjId))
                {
                    RemoveById(character, doodadObjId, messageOutput);
                }
                else
                {
                    RemoveAll(character, messageOutput);
                }
            }
            catch (Exception ex)
            {
                // Обработка исключения, например, запись в лог
                Logger.Error($"Ошибка при выполнении метода: {ex.Message}");
            }
        });
    }

    private void RemoveAll(ICharacter character, IMessageOutput messageOutput)
    {
        var nearestDoodad = GetNearestDoodad(character, 5f);
        if (nearestDoodad is null)
        {
            SendColorMessage(messageOutput, Color.Red, "Nearest Doodad does not exist");
            Logger.Warn("Nearest Doodad does not exist");
            return;
        }

        var currentWorld = WorldManager.Instance.GetWorld(((Character)character).Transform.WorldId);
        var doodadsInWorld = WorldManager.Instance.GetAllDoodadsFromWorld(currentWorld.Id);

        foreach (var doodad in doodadsInWorld.Where(d => d.TemplateId == nearestDoodad.TemplateId))
        {
            // Remove Doodad
            doodad.Spawner.Id = 0xffffffff; // removed from the game manually (укажем, что не надо сохранять в файл doodad_spawns_new.json командой /save all)
            doodad.Hide();
            SendMessage(messageOutput, $"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
            Logger.Warn($"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
        }
    }

    private void RemoveById(ICharacter character, uint doodadObjId, IMessageOutput messageOutput)
    {
        var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
        if (doodad is null)
        {
            doodad = GetNearestDoodad(character, 5f);
            if (doodad is null)
            {
                SendColorMessage(messageOutput, Color.Red, $"Doodad with objId {doodadObjId} does not exist");
                Logger.Warn($"Doodad with objId {doodadObjId} does not exist");
                return;
            }
        }

        // Remove Doodad
        doodad.Spawner.Id = 0xffffffff; // removed from the game manually (укажем, что не надо сохранять в файл doodad_spawns_new.json командой /save all)
        doodad.Hide();
        SendMessage(messageOutput, $"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
        Logger.Warn($"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
    }

    private static Doodad GetNearestDoodad(ICharacter character, float radius)
    {
        // Получаем список объектов Doodad
        var doodads = WorldManager.GetAround<Doodad>((GameObject)character, radius);

        // Инициализируем переменные для хранения ближайшего объекта и расстояния до него
        Doodad nearestDoodad = null;
        var minDistance = float.MaxValue;

        // Проходим по списку объектов
        foreach (var doodad in doodads)
        {
            // Вычисляем расстояние между персонажем и текущим объектом Doodad
            var distance = Vector3.Distance(character.Transform.World.Position, doodad.Transform.World.Position);

            // Если расстояние меньше текущего минимального, обновляем ближайший объект и минимальное расстояние
            if (!(distance < minDistance))
            {
                continue;
            }

            nearestDoodad = doodad;
            minDistance = distance;
        }

        return nearestDoodad;
    }
}
