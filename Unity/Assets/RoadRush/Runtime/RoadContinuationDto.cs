using System;using System.Collections.Generic;using System.IO;
namespace RoadRush
{
    [Serializable] public sealed class RoadContinuationDto
    {
        public string id,nodeId,roadA,roadB;public bool atEndA,atEndB;
        public const int MaximumContinuations=10000;
    }
    [Serializable] public sealed class RoadJunctionSpanDto
    {public string roadId;public double fromSourceDistance,toSourceDistance;}
}
