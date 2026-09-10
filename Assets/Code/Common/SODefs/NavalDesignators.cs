namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// Maps taxonomy enums to the historical fleet codes used for asset names and
    /// in-game text (for example USN_DD_Fletcher). Kept in one place so editor
    /// tooling, UI, and asset validation cannot drift apart.
    /// </summary>
    public static class NavalDesignators
    {
        public static string Code(ShipClass shipClass)
        {
            switch (shipClass)
            {
                case ShipClass.Frigate: return "FF";
                case ShipClass.Destroyer: return "DD";
                case ShipClass.LightCruiser: return "CL";
                case ShipClass.HeavyCruiser: return "CA";
                case ShipClass.Battlecruiser: return "BC";
                case ShipClass.Battleship: return "BB";
                case ShipClass.Carrier: return "CV";
                case ShipClass.Submarine: return "SS";
                default: return shipClass.ToString();
            }
        }

        public static string Code(Nation nation)
        {
            switch (nation)
            {
                case Nation.UnitedStates: return "USN";
                case Nation.UnitedKingdom: return "RN";
                case Nation.Germany: return "KM";
                case Nation.Japan: return "IJN";
                case Nation.France: return "MN";
                case Nation.Italy: return "RM";
                case Nation.SovietUnion: return "VMF";
                default: return nation.ToString();
            }
        }

        /// <summary>Expected asset name prefix for a hull, for example "USN_DD_".</summary>
        public static string HullPrefix(Nation nation, ShipClass shipClass)
        {
            return Code(nation) + "_" + Code(shipClass) + "_";
        }
    }
}
