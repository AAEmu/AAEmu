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

public class DoodadRemovesSubCommand : SubCommandBase
{
    public DoodadRemovesSubCommand()
    {
        Title = "[Doodad Removes]";
        Description = "Remove the nearest Doodads around a character within a <r> radius";
        CallPrefix = $"{CommandManager.CommandPrefix}removes";
        //AddParameter(new StringSubCommandParameter("radius", "radius", true, "r"));
        AddParameter(new NumericSubCommandParameter<uint>("Radius", "radius", true));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        // Запускаем метод в отдельной задаче (нити)
        Task.Run(() =>
        {
            try
            {
                if (parameters.TryGetValue("Radius", out var radius))
                {
                    RemoveAll(character, radius, messageOutput);
                }
            }
            catch (Exception ex)
            {
                // Обработка исключения, например, запись в лог
                Logger.Error($"Ошибка при выполнении метода: {ex.Message}");
            }
        });
    }

    private void RemoveAll(ICharacter character, uint radius, IMessageOutput messageOutput)
    {
        var nearestDoodad = GetNearestDoodad(character, 5f);
        if (nearestDoodad is null)
        {
            SendColorMessage(messageOutput, Color.Red, "Nearest Doodad does not exist");
            Logger.Warn("Nearest Doodad does not exist");
            return;
        }

        var doodadsInRadius = GetDoodadsInRadius(character, radius, nearestDoodad.TemplateId);
        foreach (var doodad in doodadsInRadius)
        {
            // Remove Doodad
            doodad.Spawner.Id = 0xffffffff; // removed from the game manually (укажем, что не надо сохранять в файл doodad_spawns_new.json командой /save all)
            doodad.Hide();
            SendMessage(messageOutput, $"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
            Logger.Warn($"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
        }
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

    private static List<Doodad> GetDoodadsInRadius(ICharacter character, float radius, uint templateId)
    {
        // Получаем список объектов Doodad
        var doodads = WorldManager.GetAround<Doodad>((GameObject)character, radius);

        // Фильтруем объекты, чтобы оставить только те, которые находятся в радиусе и имеют совпадающий ObjId с TemplateId
        var doodadsInRadius = doodads
            .Where(doodad => Vector3.Distance(character.Transform.World.Position, doodad.Transform.World.Position) <= radius && doodad.TemplateId == templateId)
            .ToList();

        return doodadsInRadius;
    }
}
