using System;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// What a shell is for. Declared as flags so a single value describes one shell while a
    /// combination describes what a gun is able to fire, without needing a second parallel
    /// enum for the mask.
    ///
    /// A gun therefore declares capability rather than listing specific rounds. Adding a new
    /// shell to the game means authoring one asset, not revisiting every turret that should be
    /// allowed to fire it. Compatibility is then bore diameter plus this mask, both of which
    /// already exist as data.
    /// </summary>
    [Flags]
    public enum AmmoType
    {
        None = 0,

        /// <summary>Thin walled, large burster. Wrecks unarmoured structure and starts fires, defeated by real plate.</summary>
        HighExplosive = 1 << 0,

        /// <summary>Heavy, hardened, small burster. Needs armour worth punching through to be the right choice.</summary>
        ArmorPiercing = 1 << 1,

        /// <summary>Proximity fuzed. Does not need a hit, only a near miss, so it resolves by burst radius rather than penetration.</summary>
        AntiAir = 1 << 2
    }
}
