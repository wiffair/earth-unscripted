using System;
using UnityEngine;
namespace EarthUnscripted
{
    [Serializable] public sealed class GeoRailway
    {
        public string id,start,end,name,kind,operatorName,electrified;
        public float width=3.8f,gauge=1.435f;
        public int tracks=1,layer;
        public bool bridge,tunnel,estimatedProfile=true;
        // Metres in the region's local coordinates; y is the generated rail-head level.
        public Vector3[] points;
        public string[] nodeIds;
        [NonSerialized] public float length;
    }
    public static class RailwayMetrics
    {
        public const float DefaultGauge=1.435f,DefaultWidth=3.8f,TrackSpacing=4.2f;
        public const float DeckDepth=.75f,TrackBuildUp=.45f,UnderDeckDrop=DeckDepth+TrackBuildUp;
        public const float MinimumRoadClearance=4.6f,AdditionalRampGrade=.03f;
        public const int MaximumWays=2048,MaximumPointsPerWay=4096,MaximumTotalPoints=65536;
        public static bool Supported(string kind)=>kind=="rail"||kind=="light_rail"||kind=="narrow_gauge"||kind=="tram"||kind=="subway"||kind=="monorail"||kind=="miniature"||kind=="preserved"||kind=="funicular"||kind=="disused"||kind=="abandoned"||kind=="razed"||kind=="demolished"||kind=="construction"||kind=="proposed";
        public static bool Operational(string kind)=>kind=="rail"||kind=="light_rail"||kind=="narrow_gauge"||kind=="tram"||kind=="subway"||kind=="monorail"||kind=="miniature"||kind=="preserved"||kind=="funicular";
        public static bool Visible(string kind)=>Operational(kind)||kind=="disused"||kind=="abandoned";
    }
}
