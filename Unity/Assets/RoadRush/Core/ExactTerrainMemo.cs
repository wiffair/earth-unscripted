using System;
using System.Collections.Generic;
namespace RoadRush.Core
{
    // Exact memoization only: no rounding, interpolation or sampling reduction.
    // Non-lattice coordinates are evaluated unchanged. One tile stores128² values
    // and512 validity words (67,584 payload bytes);512 tiles cap payload at33MiB.
    public sealed class ExactTerrainMemo
    {
        public const int Side=128,DefaultCapacity=512;
        sealed class Tile{public readonly float[] Values=new float[Side*Side];public readonly uint[] Valid=new uint[Side*Side/32];public LinkedListNode<ChunkCell> Node;public Tile(LinkedListNode<ChunkCell> node){Node=node;}}
        readonly int capacity;public double Spacing{get;}readonly Dictionary<ChunkCell,Tile> tiles=new Dictionary<ChunkCell,Tile>();readonly LinkedList<ChunkCell> lru=new LinkedList<ChunkCell>();
        public long Hits{get;private set;}public long Misses{get;private set;}public long Bypasses{get;private set;}public int Evictions{get;private set;}public int PeakTiles{get;private set;}public int TileCount=>tiles.Count;
        public ExactTerrainMemo(int capacity=DefaultCapacity,double spacing=2.5){if(capacity<1||capacity>DefaultCapacity)throw new ArgumentOutOfRangeException(nameof(capacity));if(double.IsNaN(spacing)||double.IsInfinity(spacing)||spacing<=0||spacing>100)throw new ArgumentOutOfRangeException(nameof(spacing));this.capacity=capacity;Spacing=spacing;}
        public void ResetStatistics(){Hits=Misses=Bypasses=0;Evictions=0;PeakTiles=tiles.Count;}
        public void Clear(){tiles.Clear();lru.Clear();ResetStatistics();}
        public float Read(float x,float z,Func<float,float,float> sample)
        {
            double gx=x/Spacing,gz=z/Spacing;
            if(double.IsNaN(gx)||double.IsInfinity(gx)||double.IsNaN(gz)||double.IsInfinity(gz)||gx<int.MinValue||gx>int.MaxValue||gz<int.MinValue||gz>int.MaxValue||gx!=Math.Truncate(gx)||gz!=Math.Truncate(gz)||BitConverter.SingleToInt32Bits(x)==int.MinValue||BitConverter.SingleToInt32Bits(z)==int.MinValue)
            {Bypasses++;return sample(x,z);}
            int ix=(int)gx,iz=(int)gz,tx=(int)Math.Floor(gx/Side),tz=(int)Math.Floor(gz/Side);var key=new ChunkCell(tx,tz);
            if(!tiles.TryGetValue(key,out var tile))
            {
                if(tiles.Count==capacity){var first=lru.First;tiles.Remove(first.Value);lru.RemoveFirst();Evictions++;}
                tile=new Tile(lru.AddLast(key));tiles.Add(key,tile);PeakTiles=Math.Max(PeakTiles,tiles.Count);
            }
            else {lru.Remove(tile.Node);lru.AddLast(tile.Node);}
            int index=(iz-tz*Side)*Side+(ix-tx*Side),word=index>>5;uint bit=1u<<(index&31);
            if((tile.Valid[word]&bit)!=0){Hits++;return tile.Values[index];}
            float result=sample(x,z);tile.Values[index]=result;tile.Valid[word]|=bit;Misses++;return result;
        }
    }
}
