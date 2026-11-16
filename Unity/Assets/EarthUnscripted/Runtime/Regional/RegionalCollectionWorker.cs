using System.Threading;
using System.Threading.Tasks;
namespace EarthUnscripted.RegionalDraft
{
    public static class RegionalCollectionWorker
    {
        // Capture paths, expected pin and place/size values on the main thread first.
        // This job has no Application, Resources, Debug, SQLite or profile access.
        public static Task<RegionalCollection.Selection> Analyze(string root,string expectedIndex,RegionalCollection.Rect window,RegionalCollection.Limits limits,CancellationToken cancellation)
        {
            if(limits==null)throw new System.ArgumentNullException(nameof(limits));
            var copy=new RegionalCollection.Limits{MaximumBuildings=limits.MaximumBuildings,MaximumBuildingPoints=limits.MaximumBuildingPoints,MaximumRoofCost=limits.MaximumRoofCost,MaximumSelectedBytes=limits.MaximumSelectedBytes};
            return Task.Run(()=>{cancellation.ThrowIfCancellationRequested();var collection=RegionalCollection.Load(root,expectedIndex);cancellation.ThrowIfCancellationRequested();return collection.QueryAll(window,copy,cancellation);},cancellation);
        }
    }
}
