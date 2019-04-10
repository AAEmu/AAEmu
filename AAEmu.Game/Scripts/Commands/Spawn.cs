using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class Spawn : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("spawn", this);
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 2)
            {
                character.SendMessage("[Spawn] /spawn <objType: npc, doodad> <unitId>");
                return;
            }

            if (uint.TryParse(args[1], out var unitId))
            {
                switch (args[0])
                {
                    case "npc":
                        if (!NpcManager.Instance.Exist(unitId))
                        {
                            character.SendMessage("[Spawn] NPC {0} don't exist", unitId);
                            return;
                        }

                        var npcSpawner = new NpcSpawner();
                        npcSpawner.Id = 0;
                        npcSpawner.UnitId = unitId;
                        npcSpawner.Position = character.Position.Clone();
                        npcSpawner.Position.Y += 3;
                        npcSpawner.SpawnAll();
                        break;
                    case "doodad":
                        if (!DoodadManager.Instance.Exist(unitId))
                        {
                            character.SendMessage("[Spawn] Doodad {0} don't exist", unitId);
                            return;
                        }

                        var doodadSpawner = new DoodadSpawner();
                        doodadSpawner.Id = 0;
                        doodadSpawner.UnitId = unitId;
                        doodadSpawner.Position = character.Position.Clone();
                        doodadSpawner.Position.Y += 3;
                        doodadSpawner.Spawn(0);
                        break;
                }
            }
            else
                character.SendMessage("[Spawn] Throw parse unitId");
        }
    }
}
