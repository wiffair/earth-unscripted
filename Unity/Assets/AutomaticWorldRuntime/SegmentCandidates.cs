using System.Collections;
using System.Collections.Generic;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // A bounded cell indexes an immutable map-owned value array. Exceptional
        // fallback cells retain their own List and cannot append to that array.
        // The reference type deliberately preserves Nearby's null contract and
        // its existing LINQ consumer; ordinary foreach uses the struct enumerator.
        sealed class SegmentCandidates:IReadOnlyList<Segment>
        {
            readonly Segment[] shared;
            readonly List<int> indices;
            readonly List<Segment> fallback;
            public SegmentCandidates(Segment[] shared,List<int> indices){this.shared=shared;this.indices=indices;}
            public SegmentCandidates(List<Segment> fallback){this.fallback=fallback;}
            public int Count=>indices?.Count??fallback.Count;
            public Segment this[int index]=>indices!=null?shared[indices[index]]:fallback[index];
            public int MembershipCapacity=>indices?.Capacity??0;
            public Enumerator GetEnumerator()=>new Enumerator(this);
            IEnumerator<Segment> IEnumerable<Segment>.GetEnumerator()=>GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
            public struct Enumerator:IEnumerator<Segment>
            {
                readonly SegmentCandidates owner;int index;
                public Enumerator(SegmentCandidates owner){this.owner=owner;index=-1;}
                public bool MoveNext()=>++index<owner.Count;
                public Segment Current=>owner[index];
                object IEnumerator.Current=>Current;
                public void Dispose(){}
                public void Reset(){index=-1;}
            }
        }
    }
}
