namespace EarthUnscripted.Core
{
    public static class RaceSetupRules
    {
        public static GarageTransaction Fit(GarageState current,int credits,RaceSetup requested)
        {
            if(current?.Active==null||current.Active.model!=3||requested==null)return null;
            var next=current.Clone();next.Active.raceSetup=requested.Clone();next.Active.raceSetup.Validate();
            next.Active.raceTyres=new RaceTyreState{compound=next.Active.raceSetup.compound};
            return new GarageTransaction(next,credits,"Race setup saved · fresh tyres fitted · no charge");
        }
    }
}
