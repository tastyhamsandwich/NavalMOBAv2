namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// Which battery a gun mount serves on a given hull. This is a property of the mount,
    /// not of the gun: a 5in/38 is the main battery on a Fletcher and the secondary
    /// battery on an Iowa. It determines which sailor slot pool crews the mount and which
    /// fire control group the player aims it with.
    /// </summary>
    public enum BatteryRole
    {
        Primary,
        Secondary
    }
}
