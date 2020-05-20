using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Transfers
{
    public class TransferTemplate
    {
        public uint Id { get; set; }     // TemplateId
        public string Name { get; set; } // comment
        public uint ModelId { get; set; }
        public double WaitTime { get; set; }
        public bool Cyclic { get; set; }
        public float PathSmoothing { get; set; }
        public List<TransferBindings> TransferBindings { get; }             // выборка по id
        public List<TransferPaths> TransferPaths { get; }                   // выборка по owner_id
        public List<TransferBindingDoodads> TransferBindingDoodads { get; } // выборка по owner_id

        public TransferTemplate()
        {
            TransferBindings = new List<TransferBindings>();
            TransferPaths = new List<TransferPaths>();
            TransferBindingDoodads = new List<TransferBindingDoodads>();
        }
    }
}
