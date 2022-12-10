using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Units
{
    //Yes the naming looks weird, ignore it please.
    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public class UnitAttributeAttribute : Attribute
    {
        public List<UnitAttribute> Attributes { get; set; }

        public UnitAttributeAttribute(params UnitAttribute[] attributes)
        {
            Attributes = new List<UnitAttribute>(attributes);
        }
    }
}
