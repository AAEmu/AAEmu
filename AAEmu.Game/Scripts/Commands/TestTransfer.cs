using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

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
            //if (args.Length < 1)
            //{
            //    character.SendMessage("[test_transfer] /test_transfer unitId (1..6{124})");
            //    return;
            //}
            //var transfer = new Transfer();
            //float newX;
            //float newY;
            //double angle;
            //sbyte newRotZ;

            //switch (args[1])
            //{
            //    case "1":
            //        transfer.TemplateId = 1; // carriage_main
            //        transfer.ModelId = 657;
            //        break;
            //    case "2":
            //        transfer.TemplateId = 2; // ferryboat
            //        transfer.ModelId = 116;
            //        break;
            //    case "3":
            //        transfer.TemplateId = 3; // Marianople Circular Float Cockpit
            //        transfer.ModelId = 606;
            //        break;
            //    case "4":
            //        transfer.TemplateId = 4; // Marianople Circulation Float
            //        transfer.ModelId = 314;
            //        break;
            //    case "5":
            //        transfer.TemplateId = 5; // Wagon 1-1
            //        transfer.ModelId = 565;
            //        break;
            //    case "6":
            //        transfer.TemplateId = 6; // Salislead Peninsula ~ Liriot Hillside Loop Carriage
            //        transfer.ModelId = 654;
            //        break;
            //}
            //transfer.ObjId = ObjectIdManager.Instance.GetNextId();
            //transfer.TlId = (ushort)TlIdManager.Instance.GetNextId();
            //transfer.Faction = FactionManager.Instance.GetFaction(143);
            //transfer.Level = 50;
            //transfer.Position = character.Position.Clone();
            //(newX, newY) = MathUtil.AddDistanceToFront(3f, character.Position.X, character.Position.Y, character.Position.RotationZ);
            //transfer.Position.Y = newY;
            //transfer.Position.X = newX;
            //angle = MathUtil.CalculateAngleFrom(transfer.Position.X, transfer.Position.Y, character.Position.X, character.Position.Y);
            //if ((args.Length <= 2) || (!sbyte.TryParse(args[2], out newRotZ)))
            //{
            //    newRotZ = MathUtil.ConvertDegreeToDirection(angle);
            //    character.SendMessage("[Spawn] Transfer {0} using angle {1}°", transfer.ModelId, angle);
            //}
            //transfer.Position.RotationX = 0;
            //transfer.Position.RotationY = 0;
            //transfer.Position.RotationZ = newRotZ;
            //transfer.MaxHp = transfer.Hp = 50000;
            //transfer.ModelParams = new UnitCustomModelParams();

            //transfer.Spawn();

            //var owner = new Transfer();
            //owner.TemplateId = 6;
            //owner.ModelId = 654;
            //owner.ObjId = ObjectIdManager.Instance.GetNextId();
            //owner.TlId = (ushort)TlIdManager.Instance.GetNextId();
            //owner.Faction = FactionManager.Instance.GetFaction(143);
            //owner.Level = 50;
            //owner.Position = character.Position.Clone();
            //(newX, newY) = MathUtil.AddDistanceToFront(3f, character.Position.X, character.Position.Y, character.Position.RotationZ);
            //owner.Position.Y = newY;
            //owner.Position.X = newX;
            //owner.Position.Z = character.Position.RotationZ;
            ////angle = MathUtil.CalculateAngleFrom(owner, owner.Position.Y, character.Position.X, character.Position.Y);
            //owner.Position.RotationX = 0;
            //owner.Position.RotationY = 0;
            ////newRotZ = MathUtil.ConvertDegreeToDirection(angle);
            ////owner.Position.RotationZ = newRotZ;
            //owner.MaxHp = owner.Hp = 50000;
            //owner.ModelParams = new UnitCustomModelParams();
            //owner.Spawn();

            //var transfer = new Transfer();
            //transfer.TemplateId = owner.Template.TransferBindings[0].TransferId; // взять из owner transferId для второй части повозки
            //transfer.ModelId = transfer.Template.ModelId;
            //transfer.ObjId = ObjectIdManager.Instance.GetNextId();
            //transfer.TlId = (ushort)TlIdManager.Instance.GetNextId();
            //transfer.BondingObjId = owner.ObjId;
            //transfer.Template.TransferBindings[0].AttachPointId = owner.Template.TransferBindings[0].AttachPointId;
            //transfer.Faction = FactionManager.Instance.GetFaction(143);
            //transfer.Level = 50;
            //transfer.Position = owner.Position.Clone();
            //(newX, newY) = MathUtil.AddDistanceToFront(0.3f, owner.Position.X, owner.Position.Y, owner.Position.RotationZ);
            //transfer.Position.Y = newY;
            //transfer.Position.X = newX;
            //owner.Position.Z = owner.Position.RotationZ;
            //owner.Position.RotationX = 0;
            //owner.Position.RotationY = 0;
            //newRotZ = owner.Position.RotationZ;
            //owner.Position.RotationZ = newRotZ;

            //transfer.MaxHp = transfer.Hp = 50000;
            //transfer.ModelParams = new UnitCustomModelParams();
            //transfer.Spawn();

        }
    }
}
