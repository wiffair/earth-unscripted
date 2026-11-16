namespace EarthUnscripted.Core
{
    public static class RaceWireRules
    {
        public static bool Valid(RaceSetup setup,RaceTyreState tyres)
        {
            if(setup==null||tyres==null)return false;
            var a=setup.Clone();a.Validate();var b=tyres.Clone();b.Validate();
            return a.finalDrive==setup.finalDrive&&a.brakeBias==setup.brakeBias&&a.spring==setup.spring&&a.differential==setup.differential&&a.frontWing==setup.frontWing&&a.rearWing==setup.rearWing&&a.compound==setup.compound&&b.temperatureC==tyres.temperatureC&&b.wear==tyres.wear&&b.compound==tyres.compound&&setup.compound==tyres.compound;
        }
    }
}
