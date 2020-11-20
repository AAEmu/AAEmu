using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Units
{
    //Yes the naming looks weird, ignore it please.
    [AttributeUsage(AttributeTargets.Property)]
    public class UnitAttributeAttribute : Attribute
    {
        public UnitAttribute Attribute { get; set; }

        public UnitAttributeAttribute(UnitAttribute attribute)
        {
            Attribute = attribute;
        }
    }
}
