using System;
using System.Collections.Generic;
namespace RoadRush.Core
{
    /// <summary>Bounded exact memo for one immutable field/build operation.
    /// No coordinate rounding, interpolation, reduced sampling or persistent
    /// cross-world cache. Value types are returned by copy. Not thread-safe.</summary>
    public sealed class ExactPointFieldMemo<T> where T:struct
    {
        public const int MaximumCapacity=262144;
        readonly Func<float,float,T> sample;
        readonly int capacity;
        readonly Dictionary<ulong,T> values=new Dictionary<ulong,T>(new CoordinateComparer());
        public long Hits{get;private set;}
        public long Evaluations{get;private set;}
        public long Bypasses{get;private set;}
        public int Count=>values.Count;
        sealed class CoordinateComparer:IEqualityComparer<ulong>
        {
            public bool Equals(ulong a,ulong b)=>a==b;
            public int GetHashCode(ulong key)
            {
                unchecked
                {
                    // Mix both float bit patterns before32bit folding. A simple
                    // XOR produces poor buckets on common mesh/grid patterns.
                    key=(key^(key>>30))*0xbf58476d1ce4e5b9UL;
                    key=(key^(key>>27))*0x94d049bb133111ebUL;
                    key^=key>>31;return(int)(key^(key>>32));
                }
            }
        }
        public ExactPointFieldMemo(Func<float,float,T> sampler,int maximumEntries)
        {
            if(sampler==null)throw new ArgumentNullException(nameof(sampler));
            if(maximumEntries<1||maximumEntries>MaximumCapacity)throw new ArgumentOutOfRangeException(nameof(maximumEntries));
            sample=sampler;capacity=maximumEntries;
        }
        public T Read(float x,float z)
        {
            if(float.IsNaN(x)||float.IsInfinity(x)||float.IsNaN(z)||float.IsInfinity(z))
            {Bypasses++;Evaluations++;return sample(x,z);}
            // Preserve signed zero and every finite input bit. The original
            // sampler may distinguish them; equality must not merge them.
            ulong key=((ulong)(uint)BitConverter.SingleToInt32Bits(x)<<32)|(uint)BitConverter.SingleToInt32Bits(z);
            if(values.TryGetValue(key,out T found)){Hits++;return found;}
            Evaluations++;T value=sample(x,z);
            if(values.Count<capacity)values.Add(key,value);else Bypasses++;
            return value;
        }
    }
}
