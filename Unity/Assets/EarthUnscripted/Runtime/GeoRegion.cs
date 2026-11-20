using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    [Serializable] public sealed class GeoRoad
    {
        public string id,start,end,name,kind; public float width,maxVehicleHeight; public int limit,oneWay,layer; public bool bridge,tunnel,roundabout; public Vector3[] points;
        [NonSerialized] public string[] sourceNodeIds;
        [NonSerialized] public float length,NativeTravelWidth,NativeForwardLimitMps,NativeBackwardLimitMps;
        [NonSerialized] public string NativeSourceWay;
        [NonSerialized] public string NativeTurnLanes,NativeTurnLanesBackward;
        [NonSerialized] public NewbridgePrototype.NewbridgeWorld.Control[] NativeControls;
    }
    [Serializable] public sealed class GeoWater { public string name; public float width; public Vector3[] points; }
    [Serializable] public sealed class GeoArea { public string kind; public Vector3[] points; }
    [Serializable] public sealed class GeoBuilding { public string id,kind,roof; public float height; public bool measuredHeight; public Vector3[] points; }
    [Serializable] public sealed class GeoTree { public string id;public float x,z,height,crownRadius;public int seed; }
    [Serializable] public sealed class GeoCanopyCoverage { public string id,sourceHash;public float minX,minZ,maxX,maxZ;public Vector3[] boundary;public int candidateCount,acceptedCount,clearanceRejectedCount,clippedCount,overlapRejectedCount,densityRejectedCount; }
    [Serializable] public sealed partial class GeoRegion
    {
        [NonSerialized] internal NewbridgePrototype.NewbridgeWorld NativeWorld;
        [NonSerialized] internal NewbridgePrototype.NewbridgeWorld.Place[] nativePlaces;
        public string PlaceNameAt(Vector3 position)
        {
            string nearest=null;float best=5000*5000;
            foreach(var place in nativePlaces??Array.Empty<NewbridgePrototype.NewbridgeWorld.Place>())
            {
                float distance=(new Vector2(place.x-position.x,place.z-position.z)).sqrMagnitude;
                if(distance<best){best=distance;nearest=place.name;}
            }
            return nearest??name??"Current area";
        }
        [NonSerialized] public AutomaticSharedSnapshot.Envelope SharedAutomatic;
        public bool AutomaticWorld => NativeWorld != null;
        public string id,name,attribution,countryCode,timeZone,datasetVersion,datasetSource,networkMapId; public bool preparedWidths; public int drivingSide=-1;public bool circuit;public float referenceLength;public string[] cornerNames;public float[] cornerDistances; public double latitude,longitude; public float datum,minX,minZ,width,depth;
        public int nx,nz,landNx,landNz; public float[] heights; public int[] landCover;
        public GeoTree[] trees=Array.Empty<GeoTree>();public GeoCanopyCoverage[] canopyCoverage=Array.Empty<GeoCanopyCoverage>();
        public GeoRoad[] roads; public GeoWater[] waters; public GeoArea[] areas; public GeoBuilding[] buildings; public Vector3[] route;
        public GeoRailway[] railways=Array.Empty<GeoRailway>();public bool railwayProfilesPrepared;
        public string railwaySourceIdentity;public bool railwaySourceComplete;
        // Experimental geometry13 runtime shadow. Persist/clone ONLY through the
        // complete World13Document envelope; legacy12 geography must stay unchanged.
        [NonSerialized] public string roadLayoutRecipe,roadSourceDigest;
        [NonSerialized] public int roadElevationSchema;
        [NonSerialized] public RoadContinuationDto[] roadContinuations=Array.Empty<RoadContinuationDto>();
        [NonSerialized] public RoadSyntheticAnchorDto[] roadSyntheticAnchors=Array.Empty<RoadSyntheticAnchorDto>();
        [NonSerialized] public RoadElevationProfileDto[] roadElevationProfiles=Array.Empty<RoadElevationProfileDto>();
        [NonSerialized] public RoadJunctionPatchDto[] roadJunctionPatches=Array.Empty<RoadJunctionPatchDto>();
        public int roadWidthSchema;public RoadWidthProfileDto[] roadWidthProfiles=Array.Empty<RoadWidthProfileDto>();
        [NonSerialized] public Packs.CircuitSceneryPlan CircuitScenery;
        [NonSerialized] public Packs.CircuitRoadSurfacePlan CircuitRoadSurfaces;
        [NonSerialized] public Vector3 ServicePad; [NonSerialized] public bool HasServicePad;
        struct Segment { public GeoRoad road; public Vector3 a,b; public float start,length,maxWidth; }
        [NonSerialized] EffectiveRoadWidthField preparedRoadWidths;
        public EffectiveRoadWidthField PreparedRoadWidths=>preparedRoadWidths;
        public float EffectiveWidth(GeoRoad road,float sourceDistance)=>PreparedRoadWidths==null?road.width:PreparedRoadWidths.At(road,sourceDistance);
        public float MaximumRoadWidth(GeoRoad road)=>PreparedRoadWidths==null?road.width:PreparedRoadWidths.Maximum(road);
        public readonly struct RoadProjection
        {public readonly GeoRoad Road;public readonly Vector3 Point;public readonly float Separation,SourceDistance,Width;public float EdgeDistance=>Separation-Width*.5f;public RoadProjection(GeoRoad road,Vector3 point,float separation,float sourceDistance,float width){Road=road;Point=point;Separation=separation;SourceDistance=sourceDistance;Width=width;}}
        public RoadProjection ProjectRoad(Vector3 position,bool includeBridges=true,bool includeTunnels=true){var road=NearestCore(position,out var point,out var distance,out var arc,out var width,includeBridges,false,includeTunnels);return new RoadProjection(road,point,distance,arc,width);}
        // Horizontal route plans have no trusted height. Use the existing 48m
        // source-cell margin for a bounded 20m projection, preserving source
        // insertion order and legal heading without scanning the whole region.
        public RoadProjection ProjectRoutePlanXZ(Vector3 position,Vector3 preferredForward)
        {
            preferredForward.y=0;preferredForward.Normalize();
            GeoRoad selected=null;Vector3 centre=position;float score=float.PositiveInfinity,arc=0,separation=float.MaxValue;
            var key=new Vector2Int(Mathf.FloorToInt(position.x/Cell),Mathf.FloorToInt(position.z/Cell));
            if(sourceCells.TryGetValue(key,out var candidates))foreach(var segment in candidates)
            {
                var delta=segment.b-segment.a;delta.y=0;
                float t=Mathf.Clamp01(Vector3.Dot(position-segment.a,delta)/Mathf.Max(.001f,delta.sqrMagnitude));
                var q=Vector3.Lerp(segment.a,segment.b,t);float dx=q.x-position.x,dz=q.z-position.z,gap=dx*dx+dz*dz;if(gap>400)continue;
                var road=segment.road;float alignment=Vector3.Dot(delta.normalized,preferredForward);
                if(road.oneWay<0)alignment=-alignment;else if(road.oneWay==0)alignment=Mathf.Abs(alignment);
                if(alignment<.65f)continue;
                float candidate=gap+(1-alignment)*25+(road.kind=="motorway"||road.kind=="motorway_link"||road.kind=="trunk"?0:25);
                if(candidate>=score)continue;score=candidate;selected=road;centre=q;arc=segment.start+segment.length*t;separation=Mathf.Sqrt(gap);
            }
            return new RoadProjection(selected,centre,separation,arc,selected==null?0:EffectiveWidth(selected,arc));
        }
        public bool IsOnRoad(Vector3 position,float margin=0,bool includeBridges=true){var p=ProjectRoad(position,includeBridges);return p.Road!=null&&p.EdgeDistance<margin;}
        public float EffectiveWidthNear(GeoRoad road,Vector3 position)
        {float start=0,best=float.MaxValue,arc=0;for(int i=1;i<road.points.Length;i++){var a=road.points[i-1];var v=road.points[i]-a;float length=v.magnitude,t=Mathf.Clamp01(((position.x-a.x)*v.x+(position.z-a.z)*v.z)/Mathf.Max(.001f,v.x*v.x+v.z*v.z));var q=a+v*t;float d=(position.x-q.x)*(position.x-q.x)+(position.z-q.z)*(position.z-q.z);if(d<best){best=d;arc=start+length*t;}start+=length;}return EffectiveWidth(road,arc);}
        // Each occurrence has one snapshot-owned value, even when two roads
        // share an ID/coordinates or the same road appears twice. Cells retain
        // original insertion order through small integer memberships.
        sealed class SegmentCell:IReadOnlyList<Segment>
        {
            readonly Segment[] shared;readonly List<int> indices;
            public SegmentCell(Segment[] values){shared=values;indices=new List<int>();}
            public SegmentCell(Segment[] values,List<int> positions){shared=values;indices=positions;}
            public int Count=>indices.Count;public int Capacity=>indices.Capacity;
            public Segment this[int index]=>shared[indices[index]];
            public void Add(int index)=>indices.Add(index);
            public Enumerator GetEnumerator()=>new Enumerator(this);
            IEnumerator<Segment> IEnumerable<Segment>.GetEnumerator()=>GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
            public struct Enumerator:IEnumerator<Segment>
            {
                readonly SegmentCell cell;int index;
                public Enumerator(SegmentCell owner){cell=owner;index=-1;}
                public bool MoveNext()=>++index<cell.Count;public Segment Current=>cell[index];object IEnumerator.Current=>Current;
                public void Dispose(){}public void Reset(){index=-1;}
            }
        }
        Segment[] querySegmentStorage=Array.Empty<Segment>();
        public int SourceCellCount=>sourceCells.Count;
        public int RoadQueryUniqueSegments=>querySegmentStorage.Length;
        public int RoadQueryCellCount=>ReferenceEquals(cells,sourceCells)?sourceCells.Count:sourceCells.Count+cells.Count;
        public long RoadQueryMemberships{get;private set;}public long RoadQueryMembershipSlots{get;private set;}
        Dictionary<Vector2Int,SegmentCell> sourceCells=new Dictionary<Vector2Int,SegmentCell>();
        Dictionary<Vector2Int,SegmentCell> cells=new Dictionary<Vector2Int,SegmentCell>();
        // Groups borrow only their own immutable cell view. Their local
        // indices retain original candidate/group order without copying values.
        readonly struct RoadSegmentView
        {
            readonly SegmentCell source;
            readonly List<int> indices;
            public RoadSegmentView(SegmentCell source){this.source=source;indices=new List<int>();}
            public void Add(int index)=>indices.Add(index);
            public Enumerator GetEnumerator()=>new Enumerator(source,indices);
            public struct Enumerator:IDisposable
            {
                readonly SegmentCell source;List<int>.Enumerator positions;
                public Enumerator(SegmentCell source,List<int> indices){this.source=source;positions=indices.GetEnumerator();}
                public Segment Current=>source[positions.Current];
                public bool MoveNext()=>positions.MoveNext();
                public void Dispose()=>positions.Dispose();
            }
        }
        sealed class RoadSamples
        {
            public GeoRoad road;public readonly RoadSegmentView segments;
            public RoadSamples(GeoRoad road,SegmentCell source){this.road=road;segments=new RoadSegmentView(source);}
        }
        Dictionary<Vector2Int,List<RoadSamples>> surfaceCells=new Dictionary<Vector2Int,List<RoadSamples>>();
        struct WaterSegment {public Vector3 a,b;public float width;}
        Dictionary<Vector2Int,List<WaterSegment>> riverCells=new Dictionary<Vector2Int,List<WaterSegment>>();
        Dictionary<string,List<GeoRoad>> junctions=new Dictionary<string,List<GeoRoad>>();
        const float Cell=80;
        public static GeoRegion Load(string id="dee-valley")
        {
            var asset=Resources.Load<TextAsset>("Geo/"+id);string source=asset?asset.text:RegionCache.Exists(id)?System.IO.File.ReadAllText(RegionCache.PathFor(id)):null;if(source==null)return null;
            return FromCachedSource(source);
        }
        internal static GeoRegion FromCachedSource(string source)
        {
            var region=JsonUtility.FromJson<GeoRegion>(source);if(region==null)throw new System.IO.InvalidDataException("Saved area is empty");if(!region.preparedWidths)foreach(var road in region.roads)road.width=region.circuit?road.width*1.35f:Mathf.Max(road.kind=="service"?6.5f:8.5f,road.width*1.4f);region.preparedWidths=true;
            region.Prepare();return region;
        }
        public GeoRegion SnapshotCopy()=>(GeoRegion)MemberwiseClone();
        public void Prepare()
        {
            // Canonical controls are authoritative. A fresh decode reconstructs
            // the same immutable evaluator before any spatial index is changed.
            EffectiveRoadWidthField finalized;
            if(roadWidthSchema==0&&(roadWidthProfiles==null||roadWidthProfiles.Length==0))finalized=null;
            else if(roadWidthSchema==1)finalized=new EffectiveRoadWidthField(roadWidthProfiles,roads);
            else throw new System.IO.InvalidDataException("Unsupported explicit road width schema");
            preparedRoadWidths=finalized;
            railways=railways??Array.Empty<GeoRailway>();
            foreach(var railway in railways){railway.length=0;if(railway.points!=null)for(int i=1;i<railway.points.Length;i++)railway.length+=Vector3.Distance(railway.points[i-1],railway.points[i]);}
            RebuildRenderQueryIndexesOnly();
        }
        bool CanShareRoadQueryIndex()
        {
            if(PreparedRoadWidths!=null)return false;
            foreach(var road in roads)
                if(float.IsNaN(road.width)||float.IsInfinity(road.width)||road.width<0||road.width>60)return false;
            return true;
        }
        // Cache-only on an exclusively owned render copy. No source normalization, solver or admission.
        public void RebuildRenderQueryIndexesOnly(bool nativeProfiles=false)
        {
            ResetEstimatedClearanceProfiles();
            ResetEstimatedRailClearances();
            // These two indexes have identical candidate order and bounds when
            // every unprofiled width gives the same 48 m search margin. Share
            // their immutable-after-build lists instead of copying each segment.
            bool sharedRoadIndex=CanShareRoadQueryIndex();
            if(sharedRoadIndex)cells=sourceCells;
            else if(ReferenceEquals(cells,sourceCells))cells=new Dictionary<Vector2Int,SegmentCell>();
            sourceCells.Clear();if(!sharedRoadIndex)cells.Clear();junctions.Clear();riverCells.Clear();surfaceCells.Clear();
            int segmentCount=0;checked{foreach(var road in roads)segmentCount+=Math.Max(0,road.points.Length-1);}
            querySegmentStorage=new Segment[segmentCount];int segmentPosition=0;
            foreach(var w in waters)for(int i=0;i<w.points.Length-1;i++)
            {
                var a=w.points[i];var b=w.points[i+1];
                for(int z=Mathf.FloorToInt((Mathf.Min(a.z,b.z)-40)/Cell);z<=Mathf.FloorToInt((Mathf.Max(a.z,b.z)+40)/Cell);z++)
                for(int x=Mathf.FloorToInt((Mathf.Min(a.x,b.x)-40)/Cell);x<=Mathf.FloorToInt((Mathf.Max(a.x,b.x)+40)/Cell);x++)
                {var key=new Vector2Int(x,z);if(!riverCells.TryGetValue(key,out var list))riverCells[key]=list=new List<WaterSegment>();list.Add(new WaterSegment{a=a,b=b,width=w.width});}
            }
            foreach(var r in roads)
            {
                r.length=0;
                foreach(string j in new[]{r.start,r.end}){if(!junctions.TryGetValue(j,out var list))junctions[j]=list=new List<GeoRoad>();list.Add(r);}
                for(int i=0;i<r.points.Length-1;i++)
                {
                    var a=r.points[i];var b=r.points[i+1];float start=r.length,length=Vector3.Distance(a,b);r.length+=length;
                    float maximum=PreparedRoadWidths==null?r.width:PreparedRoadWidths.MaximumBetween(r,start,start+length);
                    var segment=new Segment{road=r,a=a,b=b,start=start,length=length,maxWidth=maximum};
                    int sharedIndex=segmentPosition++;querySegmentStorage[sharedIndex]=segment;
                    void AddTo(Dictionary<Vector2Int,SegmentCell> index,float margin)
                    {int x0=Mathf.FloorToInt((Mathf.Min(a.x,b.x)-margin)/Cell),x1=Mathf.FloorToInt((Mathf.Max(a.x,b.x)+margin)/Cell);int z0=Mathf.FloorToInt((Mathf.Min(a.z,b.z)-margin)/Cell),z1=Mathf.FloorToInt((Mathf.Max(a.z,b.z)+margin)/Cell);for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){var key=new Vector2Int(x,z);if(!index.TryGetValue(key,out var list))index[key]=list=new SegmentCell(querySegmentStorage);list.Add(sharedIndex);}}
                    AddTo(sourceCells,48);if(!sharedRoadIndex)AddTo(cells,Mathf.Max(48,maximum*.5f+18));
                }
            }
            RoadQueryMemberships=RoadQueryMembershipSlots=0;
            foreach(var cell in sourceCells.Values){RoadQueryMemberships+=cell.Count;RoadQueryMembershipSlots+=cell.Capacity;}
            if(!sharedRoadIndex)foreach(var cell in cells.Values){RoadQueryMemberships+=cell.Count;RoadQueryMembershipSlots+=cell.Capacity;}
            foreach(var cell in sourceCells)
            {
                var groups=new Dictionary<GeoRoad,RoadSamples>();
                for(int index=0;index<cell.Value.Count;index++)
                {
                    var segment=cell.Value[index];
                    if(!groups.TryGetValue(segment.road,out var group))groups[segment.road]=group=new RoadSamples(segment.road,cell.Value);
                    group.segments.Add(index);
                }
                surfaceCells[cell.Key]=new List<RoadSamples>(groups.Values);
            }
            if(nativeProfiles || AutomaticWorld)return; // Numeric worker profiles are already authoritative.
            RebuildMainGradeJunctions();
            RebuildBridgeApproaches();
            RebuildEstimatedRailClearances();
        }
        // Main-thread publication of an independently constructed numeric query
        // snapshot. Existing Route/traffic references retain this region owner;
        // no caller can observe a cleared or partially rebuilt spatial index.
        internal void AdoptAutomaticQueries(GeoRegion snapshot)
        {
            roads=snapshot.roads;railways=snapshot.railways;waters=snapshot.waters;buildings=snapshot.buildings;
            nativePlaces=snapshot.nativePlaces;
            minX=snapshot.minX;minZ=snapshot.minZ;width=snapshot.width;depth=snapshot.depth;
            nx=snapshot.nx;nz=snapshot.nz;heights=snapshot.heights;attribution=snapshot.attribution;
            querySegmentStorage=snapshot.querySegmentStorage;RoadQueryMemberships=snapshot.RoadQueryMemberships;RoadQueryMembershipSlots=snapshot.RoadQueryMembershipSlots;
            sourceCells=snapshot.sourceCells;cells=snapshot.cells;surfaceCells=snapshot.surfaceCells;
            riverCells=snapshot.riverCells;junctions=snapshot.junctions;
        }
        public float RawHeight(float x,float z)
        {
            if(AutomaticWorld && NativeWorld.TryNaturalHeight(x,z,out float nativeHeight))return nativeHeight;
            float gx=Mathf.Clamp((x-minX)/width*(nx-1),0,nx-1),gz=Mathf.Clamp((z-minZ)/depth*(nz-1),0,nz-1);
            int ix=Mathf.Min((int)gx,nx-2),iz=Mathf.Min((int)gz,nz-2);
            return Mathf.Lerp(Mathf.Lerp(heights[iz*nx+ix],heights[iz*nx+ix+1],gx-ix),Mathf.Lerp(heights[(iz+1)*nx+ix],heights[(iz+1)*nx+ix+1],gx-ix),gz-iz);
        }
        public int Land(float x,float z)
        {
            if(landCover==null||landNx<2||landNz<2||landCover.Length!=landNx*landNz)return 0;
            int ix=Mathf.Clamp(Mathf.RoundToInt((x-minX)/width*(landNx-1)),0,landNx-1),iz=Mathf.Clamp(Mathf.RoundToInt((z-minZ)/depth*(landNz-1)),0,landNz-1);
            return landCover[iz*landNx+ix];
        }
        public GeoRoad Nearest(Vector3 position,out Vector3 point,out float separation,bool includeBridges=true)
        {return NearestCore(position,out point,out separation,out _,out _,includeBridges,false);}
        GeoRoad NearestCore(Vector3 position,out Vector3 point,out float separation,out float sourceDistance,out float effectiveWidth,bool includeBridges,bool useSourceWidths,bool includeTunnels=true)
        {
            point=position;separation=float.MaxValue;sourceDistance=0;effectiveWidth=0;float bestScore=float.MaxValue;GeoRoad result=null;
            var key=new Vector2Int(Mathf.FloorToInt(position.x/Cell),Mathf.FloorToInt(position.z/Cell));var index=useSourceWidths?sourceCells:cells;if(!index.TryGetValue(key,out var list))return null;
            foreach(var s in list)
            {
                if(!includeBridges&&IsEffectiveBridgeDeck(s.road)||!includeTunnels&&s.road.tunnel)continue;var v=s.b-s.a;var flat=new Vector2(v.x,v.z);float t=Mathf.Clamp01(Vector2.Dot(new Vector2(position.x-s.a.x,position.z-s.a.z),flat)/Mathf.Max(.001f,flat.sqrMagnitude));
                var q=Vector3.Lerp(s.a,s.b,t);float d=Vector2.Distance(new Vector2(q.x,q.z),new Vector2(position.x,position.z));float arc=s.start+s.length*t,width=useSourceWidths?s.road.width:EffectiveWidth(s.road,arc);float score=d-width*.5f;
                if(score>=bestScore)continue;bestScore=score;point=q;separation=d;sourceDistance=arc;effectiveWidth=width;result=s.road;
            }
            return result;
        }
        public bool FootprintOverlapsRoad(Vector3[] polygon,float margin=1)=>FootprintRoadOverlap(polygon,margin,null);
        public bool FootprintOverlapsRoadWithMargins(Vector3[] polygon,Func<GeoRoad,float> margin)=>FootprintRoadOverlap(polygon,1,margin);
        IEnumerable<Segment> ClearanceSegments(Segment source,float x0,float z0,float x1,float z1,float margin)
        {
            if(PreparedRoadWidths==null){yield return source;yield break;}float expansion=source.maxWidth*.5f+margin,lo=0,hi=1;var delta=source.b-source.a;
            bool Slab(float a,float d,float min,float max){if(Mathf.Abs(d)<.000001f)return a>=min&&a<=max;float l=(min-a)/d,h=(max-a)/d;if(l>h){float t=l;l=h;h=t;}lo=Mathf.Max(lo,l);hi=Mathf.Min(hi,h);return lo<=hi;}
            if(!Slab(source.a.x,delta.x,x0-expansion,x1+expansion)||!Slab(source.a.z,delta.z,z0-expansion,z1+expansion))yield break;
            int steps=Mathf.Max(1,Mathf.CeilToInt(source.length/2.5f)),first=Mathf.Max(0,Mathf.FloorToInt(lo*steps)),last=Mathf.Min(steps-1,Mathf.CeilToInt(hi*steps));if(last-first>8192)throw new System.IO.InvalidDataException("Road clearance local span budget");
            for(int i=first;i<=last;i++){float a=i/(float)steps,b=(i+1)/(float)steps;float start=source.start+source.length*a,end=source.start+source.length*b;yield return new Segment{road=source.road,a=Vector3.Lerp(source.a,source.b,a),b=Vector3.Lerp(source.a,source.b,b),start=start,length=end-start,maxWidth=PreparedRoadWidths.MaximumBetween(source.road,start,end)};}
        }
        bool FootprintRoadOverlap(Vector3[] polygon,float margin,Func<GeoRoad,float> perRoadMargin)
        {
            if(polygon==null||polygon.Length<3)return false;
            float x0=float.MaxValue,z0=x0,x1=float.MinValue,z1=x1;
            foreach(var p in polygon){x0=Mathf.Min(x0,p.x);x1=Mathf.Max(x1,p.x);z0=Mathf.Min(z0,p.z);z1=Mathf.Max(z1,p.z);}
            var visited=new HashSet<Segment>();
            for(int z=Mathf.FloorToInt(z0/Cell);z<=Mathf.FloorToInt(z1/Cell);z++)for(int x=Mathf.FloorToInt(x0/Cell);x<=Mathf.FloorToInt(x1/Cell);x++)
            {
                if(!cells.TryGetValue(new Vector2Int(x,z),out var list))continue;
                foreach(var indexed in list)
                {
                    if(!visited.Add(indexed))continue;foreach(var segment in ClearanceSegments(indexed,x0,z0,x1,z1,perRoadMargin==null?margin:perRoadMargin(indexed.road)))
                    {var a=new Vector2(segment.a.x,segment.a.z);var b=new Vector2(segment.b.x,segment.b.z);float clearance=segment.maxWidth*.5f+(perRoadMargin==null?margin:perRoadMargin(segment.road));
                    if(Mathf.Max(a.x,b.x)<x0-clearance||Mathf.Min(a.x,b.x)>x1+clearance||Mathf.Max(a.y,b.y)<z0-clearance||Mathf.Min(a.y,b.y)>z1+clearance)continue;
                    if(Contains(polygon,a)||Contains(polygon,b))return true;
                    for(int i=0;i<polygon.Length;i++)
                    {
                        var p=polygon[i];var q=polygon[(i+1)%polygon.Length];var c=new Vector2(p.x,p.z);var d=new Vector2(q.x,q.z);
                        float Cross(Vector2 u,Vector2 v)=>u.x*v.y-u.y*v.x;
                        var ab=b-a;var cd=d-c;float denom=Cross(ab,cd);
                        if(Mathf.Abs(denom)>.00001f){float t=Cross(c-a,cd)/denom,u=Cross(c-a,ab)/denom;if(t>=0&&t<=1&&u>=0&&u<=1)return true;}
                        float Dist(Vector2 v,Vector2 start,Vector2 end)=>Vector2.Distance(v,start+(end-start)*Mathf.Clamp01(Vector2.Dot(v-start,end-start)/Mathf.Max(.00001f,(end-start).sqrMagnitude)));
                        if(Mathf.Min(Mathf.Min(Dist(a,c,d),Dist(b,c,d)),Mathf.Min(Dist(c,a,b),Dist(d,a,b)))<clearance)return true;
                    }
                    }
                }
            }
            return false;
        }
        public static bool Contains(Vector3[] polygon,Vector2 point)
        {
            bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {var a=polygon[i];var b=polygon[j];if((a.z>point.y)!=(b.z>point.y)&&point.x<(b.x-a.x)*(point.y-a.z)/(b.z-a.z)+a.x)inside=!inside;}
            return inside;
        }
        public bool RailwayIntersectsCircle(Vector3 position,float radius)
        {
            var p=new Vector2(position.x,position.z);
            foreach(var track in railways??Array.Empty<GeoRailway>())if(!track.tunnel&&RailwayMetrics.Visible(track.kind))
            {
                float reach=radius+track.width*.5f;
                for(int i=1;i<track.points.Length;i++)
                {
                    var a=track.points[i-1];var b=track.points[i];if(p.x<Mathf.Min(a.x,b.x)-reach||p.x>Mathf.Max(a.x,b.x)+reach||p.y<Mathf.Min(a.z,b.z)-reach||p.y>Mathf.Max(a.z,b.z)+reach)continue;
                    var start=new Vector2(a.x,a.z);var delta=new Vector2(b.x-a.x,b.z-a.z);
                    if((p-start-delta*Mathf.Clamp01(Vector2.Dot(p-start,delta)/Mathf.Max(.000001f,delta.sqrMagnitude))).sqrMagnitude<=reach*reach)return true;
                }
            }
            return false;
        }
        public bool IsWater(float x,float z)
        {
            if(Land(x,z)==4)return true;var key=new Vector2Int(Mathf.FloorToInt(x/Cell),Mathf.FloorToInt(z/Cell));
            if(!riverCells.TryGetValue(key,out var rivers))return false;
            foreach(var w in rivers){var v=w.b-w.a;float t=Mathf.Clamp01(((x-w.a.x)*v.x+(z-w.a.z)*v.z)/Mathf.Max(.001f,v.x*v.x+v.z*v.z));var p=Vector3.Lerp(w.a,w.b,t);if(Vector2.Distance(new Vector2(x,z),new Vector2(p.x,p.z))<w.width*.5f)return true;}return false;
        }
        // A common surface for intersecting at-grade ribbons. Sampling each road's
        // independent slope at a junction made one ribbon cut through the next.
        public float RoadSurfaceHeight(float x,float z) => RoadSurfaceHeightCore(x, z, true);
        float RoadSurfaceHeightCore(float x,float z,bool joinBridges)
        {
            if(AutomaticWorld){var nativeRoad=Nearest(new Vector3(x,0,z),out var point,out _);return nativeRoad==null?RawHeight(x,z):NativeProfileHeight(nativeRoad,x,z);}
            if(CircuitRoadSurfaces!=null&&CircuitRoadSurfaces.TryHeight(x,z,out float authoredHeight))return authoredHeight;
            // Preserve the admitted vertical field and rail/bridge clearance profiles.
            var nearest=NearestCore(new Vector3(x,0,z),out var fallback,out _,out _,out _,false,true,false);
            if(nearest==null)return RawHeight(x,z);
            var key=new Vector2Int(Mathf.FloorToInt(x/Cell),Mathf.FloorToInt(z/Cell));
            if(!surfaceCells.TryGetValue(key,out var candidates))return fallback.y;
            float total=0,height=0,estimatedLift=0;
            foreach(var candidate in candidates)
            {
                var road=candidate.road;if(IsEffectiveBridgeDeck(road)||road.tunnel)continue;
                float best=float.MaxValue,y=0;
                foreach(var segment in candidate.segments)
                {
                    var a=segment.a;var delta=segment.b-a;
                    float t=Mathf.Clamp01(((x-a.x)*delta.x+(z-a.z)*delta.z)/Mathf.Max(.001f,delta.x*delta.x+delta.z*delta.z));
                    var p=a+delta*t;float d=(x-p.x)*(x-p.x)+(z-p.z)*(z-p.z);
                    if(d<best){best=d;y=p.y;}
                }
                float reach=road.width*.5f+2,weight=Mathf.Clamp01(1-Mathf.Sqrt(best)/reach);
                weight=weight*weight*(3-2*weight);total+=weight;height+=weight*y;estimatedLift+=weight*EstimatedGroundLift(road,x,z);
            }
            float blendedHeight = total > .0001f ? height / total : fallback.y;
            float preferred = PreferMainJunctionGrade(x, z, nearest.layer, blendedHeight) + (total > .0001f ? estimatedLift / total : EstimatedGroundLift(nearest,x,z));
            return joinBridges ? MatchBridgeApproach(x, z, nearest.layer, nearest, preferred) : preferred;
        }
        public float RoadSurfaceHeightOnLayer(float x,float z,int layer)
            => MatchBridgeApproach(x, z, layer, null, RoadSurfaceHeightOnLayerUnjoined(x, z, layer));
        float RoadSurfaceHeightOnLayerUnjoined(float x,float z,int layer)
        {
            if(layer==0&&CircuitRoadSurfaces!=null&&CircuitRoadSurfaces.TryHeight(x,z,out float authoredHeight))return authoredHeight;
            var key=new Vector2Int(Mathf.FloorToInt(x/Cell),Mathf.FloorToInt(z/Cell));
            if(!surfaceCells.TryGetValue(key,out var candidates))return RawHeight(x,z);
            float total=0,height=0,estimatedLift=0,bestFallback=float.MaxValue,fallback=RawHeight(x,z);
            foreach(var candidate in candidates)
            {
                var road=candidate.road;if(IsEffectiveBridgeDeck(road)||road.tunnel||road.layer!=layer)continue;float best=float.MaxValue,y=0;
                foreach(var segment in candidate.segments)
                {
                    var a=segment.a;var delta=segment.b-a;float t=Mathf.Clamp01(((x-a.x)*delta.x+(z-a.z)*delta.z)/Mathf.Max(.001f,delta.x*delta.x+delta.z*delta.z));
                    var p=a+delta*t;float d=(x-p.x)*(x-p.x)+(z-p.z)*(z-p.z);if(d<best){best=d;y=p.y;}
                }
                float edge=Mathf.Sqrt(best)-road.width*.5f;if(edge<bestFallback){bestFallback=edge;fallback=y;}
                float weight=Mathf.Clamp01(1-Mathf.Sqrt(best)/(road.width*.5f+2));weight=weight*weight*(3-2*weight);total+=weight;height+=weight*y;estimatedLift+=weight*EstimatedGroundLift(road,x,z);
            }
            float blendedHeight = total > .0001f ? height / total : fallback;
            return PreferMainJunctionGrade(x, z, layer, blendedHeight) + (total > .0001f ? estimatedLift / total : 0);
        }
        public float TerrainHeight(float x,float z) => TerrainHeightCore(x, z, true);
        float TerrainHeightCore(float x,float z,bool bridgeJoins)
        {
            if(AutomaticWorld && NativeWorld.TryGroundHeight(x,z,out float nativeHeight))return nativeHeight;
            float natural=RawHeight(x,z);
            // Authored circuits retain their reviewed terrain/pit grade; the legacy
            // free-drive garage pad must not flatten a second 25m-radius area.
            if(HasServicePad&&CircuitRoadSurfaces==null){float d=Vector2.Distance(new Vector2(x,z),new Vector2(ServicePad.x,ServicePad.z));natural=Mathf.Lerp(ServicePad.y-.12f,natural,Mathf.SmoothStep(0,1,(d-25)/14));}
            var key=new Vector2Int(Mathf.FloorToInt(x/Cell),Mathf.FloorToInt(z/Cell));
            if(riverCells.TryGetValue(key,out var rivers))foreach(var w in rivers)
            {
                var v=w.b-w.a;float t=Mathf.Clamp01(((x-w.a.x)*v.x+(z-w.a.z)*v.z)/Mathf.Max(.001f,v.x*v.x+v.z*v.z));var p=Vector3.Lerp(w.a,w.b,t);
                float bankDistance=Vector2.Distance(new Vector2(x,z),new Vector2(p.x,p.z));
                float edge=bankDistance-w.width*.5f;
                if(edge<0)natural=Mathf.Min(natural,p.y-4.2f);
                else if(edge<12)natural=Mathf.Lerp(p.y-3.08f,natural,Mathf.SmoothStep(0,1,edge/12));
            }
            // Authored circuit pavement owns its bed and boundary blend. Do
            // not excavate a second, wider legacy road footprint underneath it.
            if(CircuitRoadSurfaces!=null)
                return CircuitRoadSurfaces.TryTerrainHeight(x,z,natural,out float authoredBed)?authoredBed:natural;
            // Authored ground returned above without this unused legacy road
            // projection. Legacy excavation still uses exactly the same query.
            // Underground roads cannot suppress the supporting bed of visible surface roads.
            var projection=ProjectRoad(new Vector3(x,0,z),false,false);var r=projection.Road;var q=projection.Point;float distance=projection.Separation,roadWidth=projection.Width;
            // Follow the nearest road footprint, rather than the lowest of every
            // road within38m. The latter excavated whole neighbourhoods underneath
            // uphill roads, exposing their decks and creating artificial cliffs.
            if(r!=null&&!r.tunnel)
            {
                float edge=distance-roadWidth*.5f;
                if(edge<18)
                {
                    float bed=(bridgeJoins?RoadSurfaceHeightForRoad(r,x,z):RoadSurfaceHeightCore(x,z,false))-.55f;
                    natural=Mathf.Lerp(bed,natural,Mathf.SmoothStep(0,1,(edge-2)/16));
                }
            }
            // A roadside fill must not bury the river. Keep a narrow supporting
            // footprint beneath at-grade asphalt; outside it, the channel wins.
            if((r==null||distance>roadWidth*.5f+2)&&riverCells.TryGetValue(key,out var channels))foreach(var w in channels)
            {
                var delta=w.b-w.a;float t=Mathf.Clamp01(((x-w.a.x)*delta.x+(z-w.a.z)*delta.z)/Mathf.Max(.001f,delta.x*delta.x+delta.z*delta.z));var point=Vector3.Lerp(w.a,w.b,t);
                float edge=Vector2.Distance(new Vector2(x,z),new Vector2(point.x,point.z))-w.width*.5f;
                if(edge<0)natural=Mathf.Min(natural,point.y-4.2f);
                else if(edge<4)natural=Mathf.Min(natural,Mathf.Lerp(point.y-3.08f,natural,Mathf.SmoothStep(0,1,edge/4)));
            }
            if(bridgeJoins&&TryBridgeTerrainCeiling(x,z,out float ceiling))
                natural=Mathf.Min(natural,Mathf.Min(ceiling,TerrainHeightCore(x,z,false)));
            return bridgeJoins?ApplyEstimatedRailCut(x,z,natural):natural;
        }
        public float JunctionRadius(string id,float fallback,int? layer=null)
        {if(!junctions.TryGetValue(id,out var connected))return fallback;foreach(var road in connected)if(!road.tunnel&&(IsEffectiveBridgeDeck(road)||!layer.HasValue||road.layer==layer.Value)){if(road.start==id)fallback=Mathf.Max(fallback,EffectiveWidth(road,0)*.5f*(IsEffectiveBridgeDeck(road)?BridgeCapCircleScale:1));if(road.end==id)fallback=Mathf.Max(fallback,EffectiveWidth(road,road.length)*.5f*(IsEffectiveBridgeDeck(road)?BridgeCapCircleScale:1));}return fallback;}
        public IReadOnlyList<GeoRoad> RoadsAtJunction(string node)=>junctions.TryGetValue(node,out var roads)?roads:Array.Empty<GeoRoad>();
        public GeoRoad Next(GeoRoad current,ref int direction,System.Random random)
        {
            string end=direction>0?current.end:current.start;if(!junctions.TryGetValue(end,out var options))return current;
            var valid=new List<GeoRoad>();foreach(var r in options)if(r!=current&&(!AutomaticWorld||r.id!=current.id)&&(!r.tunnel||AutomaticWorld)&&(r.start==end?r.oneWay!=-1:r.oneWay!=1))valid.Add(r);
            if(AutomaticWorld&&valid.Count>0)
            {
                string lanes=direction>0?current.NativeTurnLanes:current.NativeTurnLanesBackward;
                if(!string.IsNullOrEmpty(lanes))
                {
                    var choices=lanes.Split('|');string allowed=choices[drivingSide<0?0:choices.Length-1];
                    if(allowed!=""&&allowed!="none")
                    {
                        int travel=direction;RoadPoint(current,travel>0?current.length:0,out var incoming);incoming*=travel;
                        var legal=valid.FindAll(r=>{int nextTravel=r.start==end?1:-1;RoadPoint(r,nextTravel>0?0:r.length,out var outgoing);float angle=Vector3.SignedAngle(incoming,outgoing*nextTravel,Vector3.up);return Math.Abs(angle)<35?allowed.Contains("through"):angle>0?allowed.Contains("right"):allowed.Contains("left");});
                        if(legal.Count>0)valid=legal;
                    }
                }
            }
            if(valid.Count==0){if(current.oneWay==0){direction=-direction;return current;}return null;}
            var next=valid[random.Next(valid.Count)];direction=next.start==end?1:-1;return next;
        }
        public float RoadProgress(GeoRoad r,Vector3 p)
        {
            float progress=0,best=float.MaxValue,result=0;
            for(int i=0;i<r.points.Length-1;i++)
            {var delta=r.points[i+1]-r.points[i];float length=delta.magnitude;float t=Mathf.Clamp01(Vector3.Dot(p-r.points[i],delta)/Mathf.Max(.001f,delta.sqrMagnitude));float error=(p-(r.points[i]+delta*t)).sqrMagnitude;if(error<best){best=error;result=progress+length*t;}progress+=length;}
            return result;
        }
        public Vector3 DrivingLanePoint(GeoRoad road,Vector3 centre,Vector3 preferredForward,out Vector3 forward)
        {
            float sourceDistance=RoadProgress(road,centre);RoadPoint(road,sourceDistance,out forward);
            if(road.oneWay<0||road.oneWay==0&&Vector3.Dot(preferredForward,forward)<0)forward=-forward;
            float lane=circuit?0:Mathf.Min(2.7f,(road.NativeTravelWidth>0?road.NativeTravelWidth:EffectiveWidth(road,sourceDistance))*.24f)*drivingSide;
            var point=centre+Vector3.Cross(Vector3.up,forward).normalized*lane;if(!road.tunnel)point.y=RoadSurfaceHeightForRoad(road,point.x,point.z);return point;
        }
        public Vector3 RoadPoint(GeoRoad r,float d,out Vector3 forward)
        {
            d=Mathf.Clamp(d,0,r.length);for(int i=0;i<r.points.Length-1;i++)
            {var delta=r.points[i+1]-r.points[i];float length=delta.magnitude;if(d<=length||i==r.points.Length-2){forward=delta.normalized;var point=r.points[i]+forward*Mathf.Min(d,length);
                if(IsEffectiveBridgeDeck(r)&&!r.tunnel)
                {
                    point.y=EffectiveBridgeHeight(r,point.x,point.z);
                    // Height hints use the derived deck; station/XY remain the
                    // original source arc. Vehicle physics still owns its pose.
                    var flat=new Vector2(forward.x,forward.z).normalized;
                    float slope=EffectiveBridgeHeight(r,point.x+flat.x*.5f,point.z+flat.y*.5f)-EffectiveBridgeHeight(r,point.x-flat.x*.5f,point.z-flat.y*.5f);
                    forward=new Vector3(flat.x,slope,flat.y).normalized;
                }
                return point;}d-=length;}
            forward=Vector3.forward;return r.points[0];
        }
    }
}
