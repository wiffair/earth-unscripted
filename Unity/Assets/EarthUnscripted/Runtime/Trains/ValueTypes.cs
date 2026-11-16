using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
namespace EarthUnscripted.Candidates.Compatibility {
 // A scalar value; no array is retained or returned. Double precision throughout.
 public readonly struct Vec3 : IReadOnlyList<double> {
  public double X {get;} public double Y {get;} public double Z {get;}
  public Vec3(double x,double y,double z){X=x;Y=y;Z=z;}
  public int Count=>3;public int Length=>3;
  public double this[int i]=>i==0?X:i==1?Y:i==2?Z:throw new ArgumentOutOfRangeException(nameof(i));
  public void CopyTo(double[] target,int offset=0){if(target==null)throw new ArgumentNullException(nameof(target));if(offset<0||offset>target.Length-3)throw new ArgumentOutOfRangeException(nameof(offset));target[offset]=X;target[offset+1]=Y;target[offset+2]=Z;}
  public double[] ToArray()=>new[]{X,Y,Z};
  public static implicit operator Vec3(double[] value){if(value==null||value.Length!=3)throw new ArgumentException("three vector scalars required");return new Vec3(value[0],value[1],value[2]);}
  public Enumerator GetEnumerator()=>new Enumerator(this);
  IEnumerator<double> IEnumerable<double>.GetEnumerator()=>new Enumerator(this);IEnumerator IEnumerable.GetEnumerator()=>new Enumerator(this);
  public struct Enumerator:IEnumerator<double>{readonly Vec3 value;int index;internal Enumerator(Vec3 value){this.value=value;index=-1;}public double Current=>value[index];object IEnumerator.Current=>Current;public bool MoveNext()=>++index<3;public void Reset(){index=-1;}public void Dispose(){}}
 }
 // Sealed ownership wrapper. Public construction copies input; index access returns immutable/value DTOs.
 public sealed class FrozenList<T>:IReadOnlyList<T>{
  readonly T[] data;
  public FrozenList(IEnumerable<T> input){if(input==null)throw new ArgumentNullException(nameof(input));data=input.ToArray();}
  private FrozenList(T[] owned,bool takeOwnership){data=owned;}
  internal static FrozenList<T> Own(T[] privateArray)=>new FrozenList<T>(privateArray,true);
  public int Count=>data.Length;public int Length=>data.Length;public T this[int i]=>data[i];
  public void CopyTo(T[] target,int offset=0){if(target==null)throw new ArgumentNullException(nameof(target));if(offset<0||offset>target.Length-data.Length)throw new ArgumentOutOfRangeException(nameof(offset));Array.Copy(data,0,target,offset,data.Length);}
  public T[] ToArray()=>(T[])data.Clone();
  public static implicit operator FrozenList<T>(T[] array)=>new FrozenList<T>(array);
  public Enumerator GetEnumerator()=>new Enumerator(data);
  IEnumerator<T> IEnumerable<T>.GetEnumerator()=>new Enumerator(data);IEnumerator IEnumerable.GetEnumerator()=>new Enumerator(data);
  public struct Enumerator:IEnumerator<T>{readonly T[] source;int index;internal Enumerator(T[] source){this.source=source;index=-1;}public T Current=>source[index];object IEnumerator.Current=>Current!;public bool MoveNext()=>++index<source.Length;public void Reset(){index=-1;}public void Dispose(){}}
 }
}
