using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestTransfer : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "testtransfer", "test_transfer" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Spawns a transportation vehicle";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 1)
            {
                character.SendMessage("[test_transfer] /test_transfer unitId (1..6{124})");
                return;
            }
            var transfer = new Transfer();
            float newX;
            float newY;
            double angle;
            sbyte newRotZ;

            switch (args[1])
            {
                case "1":
                    transfer.TemplateId = 1; // carriage_main
                    transfer.ModelId = 657;
                    break;
                case "2":
                    transfer.TemplateId = 2; // ferryboat
                    transfer.ModelId = 116;
                    break;
                case "3":
                    transfer.TemplateId = 3; // Marianople Circular Float Cockpit
                    transfer.ModelId = 606;
                    break;
                case "4":
                    transfer.TemplateId = 4; // Marianople Circulation Float
                    transfer.ModelId = 314;
                    break;
                case "5":
                    transfer.TemplateId = 5; // Wagon 1-1
                    transfer.ModelId = 565;
                    break;
                case "6":
                    transfer.TemplateId = 6; // Salislead Peninsula ~ Liriot Hillside Loop Carriage
                    transfer.ModelId = 654;
                    break;
            }
            transfer.ObjId = ObjectIdManager.Instance.GetNextId();
            transfer.TlId = (ushort)TlIdManager.Instance.GetNextId();
            transfer.Faction = FactionManager.Instance.GetFaction(143);
            transfer.Level = 50;
            transfer.Position = character.Position.Clone();
            (newX, newY) = MathUtil.AddDistanceToFront(3f, character.Position.X, character.Position.Y, character.Position.RotationZ);
            transfer.Position.Y = newY;
            transfer.Position.X = newX;
            angle = MathUtil.CalculateAngleFrom(transfer.Position.X, transfer.Position.Y, character.Position.X, character.Position.Y);
            if ((args.Length <= 2) || (!sbyte.TryParse(args[2], out newRotZ)))
            {
                newRotZ = MathUtil.ConvertDegreeToDirection(angle);
                character.SendMessage("[Spawn] Transfer {0} using angle {1}°", transfer.ModelId, angle);
            }
            transfer.Position.RotationX = 0;
            transfer.Position.RotationY = 0;
            transfer.Position.RotationZ = newRotZ;
            transfer.MaxHp = transfer.Hp = 50000;
            transfer.ModelParams = new UnitCustomModelParams();

            transfer.Spawn();
        }
    }
}
