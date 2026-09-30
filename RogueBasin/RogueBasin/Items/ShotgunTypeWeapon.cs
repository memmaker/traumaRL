using System;
using System.Collections.Generic;
using System.Text;
using libtcodWrapper;

namespace RogueBasin.Items
{
    [System.Serializable] public abstract class ShotgunTypeWeapon : RangedWeapon
    {
        public virtual double ShotgunSpreadAngle()
        {
            return Math.PI / 4;
        }
    }
}
