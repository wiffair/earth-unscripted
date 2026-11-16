using System;using System.Collections.Generic;using System.IO;using System.Linq;using Row=EarthUnscripted.TerrainDraft.Core800.SourceRoadRow;
namespace EarthUnscripted.Core800
{
    public enum RoadRowSide {Previous=0,Next=1}
    // Pure station mapping, not geometry admission. Keeps explicit one-sided
    // frames at a duplicate S/H knot; NEVER Distincts or crosses a zero interval.
    public sealed class SideAwareRoadRows800
    {
        public readonly struct Frame
        {
            public readonly double SourceS,DerivedH,CenterX,CenterZ,RightX,RightZ,DCenterXPerH,DCenterZPerH,DRightXPerH,DRightZPerH;
            public readonly bool HasLongitudinal;public readonly RoadRowSide SelectedSide;
            internal Frame(double s,double h,double x,double z,double rx,double rz,bool derivative,double dx,double dz,double drx,double drz,RoadRowSide side){SourceS=s;DerivedH=h;CenterX=x;CenterZ=z;RightX=rx;RightZ=rz;HasLongitudinal=derivative;DCenterXPerH=dx;DCenterZPerH=dz;DRightXPerH=drx;DRightZPerH=drz;SelectedSide=side;}
        }
        readonly Dictionary<string,Row[]> rows=new Dictionary<string,Row[]>(StringComparer.Ordinal);
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        static void Need(bool okay,string why){if(!okay)throw new InvalidDataException("One-sided800 source rows: "+why);}
        public SideAwareRoadRows800(IReadOnlyDictionary<string,Row[]> source)
        {
            Need(source!=null&&source.Count>0&&source.Count<=128,"source-map budget");int count=0;
            foreach(var pair in source)
            {
                Need(!string.IsNullOrEmpty(pair.Key)&&pair.Key.Length<=128&&pair.Value!=null&&pair.Value.Length>=2&&pair.Value.Length<=16384&&(count+=pair.Value.Length)<=100000,"row identity/count budget");var copy=(Row[])pair.Value.Clone();
                for(int i=0;i<copy.Length;i++)
                {
                    var r=copy[i];Need(Finite(r.SourceS)&&Finite(r.DerivedH)&&r.SourceS>=0&&r.DerivedH>=0&&Finite(r.CenterX)&&Finite(r.CenterY)&&Finite(r.CenterZ)&&Finite(r.RightX)&&Finite(r.RightY)&&Finite(r.RightZ),"finite rows");
                    if(i>0){var p=copy[i-1];Need(r.SourceS>=p.SourceS&&r.DerivedH>=p.DerivedH,"monotone literal stations");bool sameS=r.SourceS==p.SourceS,sameH=r.DerivedH==p.DerivedH;Need(sameS==sameH,"zero source interval cannot hide positive derived length");if(sameS)Need(p.PreviousSide&&!r.PreviousSide&&(i<2||copy[i-2].SourceS!=r.SourceS),"only one explicit incoming→outgoing pair");}
                    if(r.PreviousSide)Need(i+1<copy.Length&&copy[i+1].SourceS==r.SourceS&&copy[i+1].DerivedH==r.DerivedH&&!copy[i+1].PreviousSide,"incoming row requires explicit same-station outgoing partner");
                }
                rows.Add(pair.Key,copy);
            }
        }
        public Row[] CopyRows(string roadId)=>roadId!=null&&rows.TryGetValue(roadId,out var r)?(Row[])r.Clone():Array.Empty<Row>();
        public bool TryFrame(string roadId,double sourceS,RoadRowSide side,out Frame frame)
        {
            frame=default;if(!Finite(sourceS)||(side!=RoadRowSide.Previous&&side!=RoadRowSide.Next)||roadId==null||!rows.TryGetValue(roadId,out var r)||sourceS<r[0].SourceS||sourceS>r[r.Length-1].SourceS)return false;
            int lo=0,hi=r.Length;while(lo<hi){int mid=(lo+hi)/2;if(r[mid].SourceS<sourceS)lo=mid+1;else hi=mid;}int first=lo;
            if(first<r.Length&&r[first].SourceS==sourceS)
            {
                int last=first;if(last+1<r.Length&&r[last+1].SourceS==sourceS)last++;int selected=side==RoadRowSide.Previous?first:last;
                int a=side==RoadRowSide.Previous?selected-1:selected,b=side==RoadRowSide.Previous?selected:selected+1;bool derivative=a>=0&&b<r.Length&&r[b].SourceS>r[a].SourceS&&r[b].DerivedH>r[a].DerivedH;var at=r[selected];double dh=derivative?r[b].DerivedH-r[a].DerivedH:1;
                frame=new Frame(sourceS,at.DerivedH,at.CenterX,at.CenterZ,at.RightX,at.RightZ,derivative,derivative?((double)r[b].CenterX-r[a].CenterX)/dh:0,derivative?((double)r[b].CenterZ-r[a].CenterZ)/dh:0,derivative?((double)r[b].RightX-r[a].RightX)/dh:0,derivative?((double)r[b].RightZ-r[a].RightZ)/dh:0,side);return true;
            }
            int left=first-1,right=first;Need(left>=0&&right<r.Length&&r[right].SourceS>r[left].SourceS&&r[right].DerivedH>r[left].DerivedH,"positive interpolation span");double t=(sourceS-r[left].SourceS)/(r[right].SourceS-r[left].SourceS),h=r[right].DerivedH-r[left].DerivedH;double L(float a,float b)=>a+((double)b-a)*t;
            frame=new Frame(sourceS,r[left].DerivedH+h*t,L(r[left].CenterX,r[right].CenterX),L(r[left].CenterZ,r[right].CenterZ),L(r[left].RightX,r[right].RightX),L(r[left].RightZ,r[right].RightZ),true,((double)r[right].CenterX-r[left].CenterX)/h,((double)r[right].CenterZ-r[left].CenterZ)/h,((double)r[right].RightX-r[left].RightX)/h,((double)r[right].RightZ-r[left].RightZ)/h,side);return true;
        }
    }
}
