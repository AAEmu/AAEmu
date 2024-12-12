using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils;
using System.Globalization;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class SpawnGrid : ICommand
{
    public string[] CommandNames { get; set; } = ["spawngrid", "spawngroup"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<npc||doodad> <templateID> <columns> <rows> <spacing>";
    }

    public string GetCommandHelpText()
    {
        return
            "Spawns a large amount of NPCs or doodads using <templateID> as a template in a grid in front of you using specified number of columns, rows and spacing.\n" +
            $"Example: {CommandManager.CommandPrefix}{CommandNames[0]} doodad 320 5 5 2";
    }

    public static void SpawnDoodad(uint unitId, Character character, float newX, float newY)
    {
        var doodadSpawner = new DoodadSpawner();
        doodadSpawner.Id = 0;
        doodadSpawner.UnitId = unitId;
        doodadSpawner.Position = character.Transform.CloneAsSpawnPosition();
        doodadSpawner.Position.Y = newY;
        doodadSpawner.Position.X = newX;
        var angle = (float)MathUtil.CalculateAngleFrom(doodadSpawner.Position.X, doodadSpawner.Position.Y,
            character.Transform.World.Position.X, character.Transform.World.Position.Y);
        doodadSpawner.Position.Yaw =
            angle.DegToRad(); // TODO: this seems wrong for now, will need to replace with a LookAt() at some later point
        doodadSpawner.Position.Pitch = 0;
        doodadSpawner.Position.Roll = 0;
        doodadSpawner.Spawn(0);
        // character.SendMessage(doodadSpawner.Position.ToString());
    }

    public static void SpawnNPC(uint unitId, Character character, float newX, float newY)
    {
        var npcSpawner = new NpcSpawner();
        npcSpawner.Id = 0;
        npcSpawner.UnitId = unitId;
        npcSpawner.Position = character.Transform.CloneAsSpawnPosition();
        npcSpawner.Position.Y = newY;
        npcSpawner.Position.X = newX;
        var angle = (float)MathUtil.CalculateAngleFrom(npcSpawner.Position.X, npcSpawner.Position.Y,
            character.Transform.World.Position.X, character.Transform.World.Position.Y);
        npcSpawner.Position.Yaw = angle.DegToRad();
        npcSpawner.Position.Pitch = 0;
        npcSpawner.Position.Roll = 0;

        SpawnManager.Instance.AddNpcSpawner(npcSpawner);
        npcSpawner.SpawnAll();
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 5)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var action = args[0].ToLower();
        if (!uint.TryParse(args[1], out var templateId) || !uint.TryParse(args[2], out var columns) ||
            !uint.TryParse(args[3], out var rows) ||
            !float.TryParse(args[4], CultureInfo.InvariantCulture, out var spacing))
        {
            CommandManager.SendErrorText(this, messageOutput, "Arguments parse error");
            return;
        }

        if (columns < 1)
        {
            columns = 1;
        }

        if (rows < 1)
        {
            rows = 1;
        }

        if (spacing < 0.1f)
        {
            spacing = 0.1f;
        }

        switch (action)
        {
            case "npc":
                if (!NpcManager.Instance.Exist(templateId))
                {
                    CommandManager.SendErrorText(this, messageOutput, $"NPC {templateId} don't exist");
                    return;
                }

                break;
            case "doodad":
                if (!DoodadManager.Instance.Exist(templateId))
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Doodad {templateId} don't exist");
                    return;
                }

                break;
            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown object type {action}");
                return;
        }

        // Origin point for spawns
        var (startX, startY) = MathUtil.AddDistanceToFront(3f, character.Transform.World.Position.X,
            character.Transform.World.Position.Y, character.Transform.World.Rotation.Z);
        for (var y = 0; y < rows; y++)
        {
            var sizeY = rows * spacing;
            var posY = (y + 1) * spacing;
            for (var x = 0; x < columns; x++)
            {
                var sizeX = columns * spacing;
                var posX = x * spacing - sizeX / 2;
                using var newPos = character.Transform.CloneDetached();
                newPos.Local.AddDistanceToFront(posY);
                newPos.Local.AddDistanceToRight(posX);
                switch (action)
                {
                    case "npc":
                        SpawnNPC(templateId, character, newPos.World.Position.X, newPos.World.Position.Y);
                        break;
                    case "doodad":
                        SpawnDoodad(templateId, character, newPos.World.Position.X, newPos.World.Position.Y);
                        break;
                }
            }
        }
    }
}
