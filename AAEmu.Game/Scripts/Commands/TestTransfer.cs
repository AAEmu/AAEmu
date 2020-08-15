using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using AAEmu.Game.Models;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestTransfer : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "tr", "testtransfer", "test_transfer" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Spawns a transferation vehicle";
        }

        public void Execute(Character character, string[] args)
        {
            float newX;
            float newY;
            double angle;
            sbyte newRotZ;

            var transfer = new Transfer();
            transfer.TemplateId = 6;
            transfer.ModelId = 654;

            transfer.Position = character.Position.Clone();
            // спавним спереди себя на 3 метра
            (newX, newY) = MathUtil.AddDistanceToFront(3f, character.Position.X, character.Position.Y, character.Position.RotationZ);
            transfer.Position.Y = newY;
            transfer.Position.X = newX;
            transfer.Position.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(transfer.Position.ZoneId, transfer.Position.X, transfer.Position.Y) : character.Position.Z;
            character.SendMessage("[Spawn] Transfer {0} X={1}, Y={2}, Z={3}", transfer.ModelId, transfer.Position.X, transfer.Position.Y, transfer.Position.Z);

            angle = MathUtil.CalculateAngleFrom(transfer.Position.X, transfer.Position.Y, character.Position.X, character.Position.Y);
            if ((args.Length <= 2) || (!sbyte.TryParse(args[2], out newRotZ)))
            {
                newRotZ = MathUtil.ConvertDegreeToDirection(angle);
                character.SendMessage("[Spawn] Transfer {0} using angle {1}°", transfer.ModelId, angle);
            }
            // character.Position.Z
            transfer.Position.RotationX = 0;
            transfer.Position.RotationY = 0;
            transfer.Position.RotationZ = 63;

            transfer.MaxHp = transfer.Hp;
            transfer.MaxMp = transfer.Mp;

            transfer.Spawn();
        }
    }
}
