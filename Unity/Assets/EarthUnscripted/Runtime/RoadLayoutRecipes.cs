using System;
namespace EarthUnscripted
{
    public static class RoadLayoutRecipes
    {
        public const string PreservedWays="preserved-ways-v1",VerifiedLegacyCuts="verified-legacy-cuts-v1",Authored="authored-v1";
        public static bool Known(string value)=>value==PreservedWays||value==VerifiedLegacyCuts||value==Authored;
    }
}
