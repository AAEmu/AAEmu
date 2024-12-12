using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
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

public class Spawn : ICommand
{
    public string[] CommandNames { get; set; } = ["spawn"];
    private const uint DUMMY_NPC_TEMPLATE_ID = 7512;

    // Unused protected static Logger Logger = LogManager.GetCurrentClassLogger();
    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<npc||doodad||remove> <unitId> [rotationZ]";
    }

    public string GetCommandHelpText()
    {
        return "Spawns a npc or doodad using <unitId> as a template. Or remove a doodad";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var unitId = 0u;

        if (args[1].Equals("dummy", System.StringComparison.CurrentCultureIgnoreCase) && args[0] == "npc")
        {
            unitId = DUMMY_NPC_TEMPLATE_ID;
        }
        else if (!uint.TryParse(args[1], out unitId))
        {
            CommandManager.SendErrorText(this, messageOutput, $"<unitId> parse error");
            return;
        }

        if (unitId > 0)
        {
            float angle;
            using var charPos = character.Transform.CloneDetached();
            switch (args[0])
            {
                case "remove":
                    var myDoodad = WorldManager.Instance.GetDoodad(unitId);

                    if (myDoodad != null && myDoodad is Doodad)
                    {
                        CommandManager.SendNormalText(this, messageOutput, $"Removing Doodad with ID {myDoodad.ObjId}");
                        ObjectIdManager.Instance.ReleaseId(myDoodad.ObjId);
                        myDoodad.Delete();
                    }
                    else
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"Doodad with Id {unitId} Does not exist");
                    }

                    break;
                case "npc":
                    if (!NpcManager.Instance.Exist(unitId))
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"NPC {unitId} don't exist|r");
                        return;
                    }

                    var npcSpawner = new NpcSpawner();
                    npcSpawner.Id = 0;
                    npcSpawner.UnitId = unitId;
                    charPos.Local.AddDistanceToFront(3f);
                    angle = (float)MathUtil.CalculateAngleFrom(charPos, character.Transform);
                    npcSpawner.Position = charPos.CloneAsSpawnPosition();

                    if (args.Length > 2 && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var newRotZ))
                    {
                        angle = newRotZ.DegToRad();
                        CommandManager.SendNormalText(this, messageOutput,
                            $"Spawn NPC {unitId} using angle {newRotZ}° = {angle} rad");
                    }
                    else
                    {
                        angle = angle.DegToRad();
                        CommandManager.SendNormalText(this, messageOutput,
                            $"Spawn NPC {unitId} facing you using angle {angle} rad");
                    }

                    npcSpawner.Position.Yaw = angle;
                    npcSpawner.Position.Pitch = 0;
                    npcSpawner.Position.Roll = 0;

                    SpawnManager.Instance.AddNpcSpawner(npcSpawner);

                    npcSpawner.SpawnAll();
                    // CommandManager.SendNormalText(this, messageOutput, "[Spawn] NPC {0} spawned with angle {1}", unitId, angle);
                    break;
                case "doodad":
                    if (!DoodadManager.Instance.Exist(unitId))
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"Doodad {unitId} don't exist");
                        return;
                    }

                    var doodadSpawner = new DoodadSpawner();
                    doodadSpawner.Id = 0;
                    doodadSpawner.UnitId = unitId;
                    charPos.Local.AddDistanceToFront(3f);
                    angle = (float)MathUtil.CalculateAngleFrom(charPos, character.Transform);
                    doodadSpawner.Position = charPos.CloneAsSpawnPosition();
                    //(newX, newY) = MathUtil.AddDistanceToFront(3f, character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.Rotation.Z);
                    //doodadSpawner.Position.Y = newY;
                    //doodadSpawner.Position.X = newX;
                    //angle = (float)MathUtil.CalculateAngleFrom(doodadSpawner.Position.Y, doodadSpawner.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.X);
                    if (args.Length > 2 && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var degrees))
                    {
                        angle = degrees.DegToRad();
                        CommandManager.SendNormalText(this, messageOutput,
                            $"Spawn Doodad {unitId} using user provided angle {degrees}° = {angle} rad");
                    }
                    else
                    {
                        angle = angle.DegToRad();
                        CommandManager.SendNormalText(this, messageOutput,
                            $"Spawn Doodad {unitId} facing you, using characters angle {angle}");
                    }

                    doodadSpawner.Position.Yaw = angle;
                    doodadSpawner.Position.Pitch = 0;
                    doodadSpawner.Position.Roll = 0;
                    doodadSpawner.Spawn(0, 0, character.ObjId);
                    break;
            }
        }
        else
        {
            CommandManager.SendNormalText(this, messageOutput, "unitId can not be 0");
        }
    }
}
