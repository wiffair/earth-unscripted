using System;using System.IO;using UnityEditor;using UnityEngine;
namespace EarthUnscripted.Editor
{
 public static class RoadSourceCommitmentVerification
 {
  static int checks;
  static void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);checks++;Debug.Log("SOURCE COMMITMENT PASS "+why);}
  static float NegativeZero=>BitConverter.ToSingle(new byte[]{0,0,0,128},0);
  static GeoRoad Known()=>new GeoRoad{id="road-α",start="n0",end="n1",name="Rhiw'r Ddraig",kind="service",width=1.25f,maxVehicleHeight=NegativeZero,limit=30,oneWay=-1,layer=0,points=new[]{new Vector3(0,NegativeZero,0),new Vector3(20,1,-3)},sourceNodeIds=new[]{"n0","n1"}};
  static string Digest(GeoRoad r)=>RoadSourceIdentity.Compute(new[]{r});
  static void Mutation(Action<GeoRoad> change,string name,string original){var r=Known();change(r);Check(Digest(r)!=original,"source digest binds "+name);}
  public static void Run()
  {
   int exit=1;checks=0;try
   {
    Check(!Application.isPlaying,"isolated Editor commitment, no session/career opened");string original=Digest(Known());Check(original=="0e8b9e9ac0731b76120a5bd0bc19aa80752d0ce87a79b0c1af017bd40a27f30b","exact independent Python little-endian IEEE754/UTF8 test vector");
    Mutation(r=>r.width=1.2500001f,"raw width float bits",original);Mutation(r=>r.maxVehicleHeight=0,"negative-zero height-limit bits",original);Mutation(r=>r.name="Changed","display name",original);Mutation(r=>r.kind="primary","road class",original);Mutation(r=>r.limit=31,"speed limit",original);Mutation(r=>r.oneWay=1,"direction",original);Mutation(r=>r.layer=1,"level",original);Mutation(r=>r.bridge=true,"bridge flag",original);Mutation(r=>r.tunnel=true,"tunnel flag",original);Mutation(r=>r.points[1].x=20.125f,"raw coordinate",original);Mutation(r=>{r.end="n2";r.sourceNodeIds[1]="n2";},"node identity",original);
    var nullName=Known();nullName.name=null;var emptyName=Known();emptyName.name="";Check(Digest(nullName)!=Digest(emptyName),"null/empty source provenance stays distinct");var cached=Known();cached.length=999;Check(Digest(cached)==original,"derived cached length excluded");var a=Known();var b=Known();b.id="second";Check(RoadSourceIdentity.Compute(new[]{a,b})==RoadSourceIdentity.Compute(new[]{b,a}),"road enumeration order does not alter source commitment");
    bool rejected=false;var bad=Known();bad.name=new string('x',2049);try{Digest(bad);}catch(InvalidDataException){rejected=true;}Check(rejected,"source text bounded before hashing");rejected=false;bad=Known();bad.width=float.NaN;try{Digest(bad);}catch(InvalidDataException){rejected=true;}Check(rejected,"nonfinite source scalar rejected");
    Directory.CreateDirectory("Builds/Verification/World13Foundation");File.WriteAllText("Builds/Verification/World13Foundation/source-v2-unity.txt","PASS "+checks+" actual Unity source-v2 commitment; independent bit-vector and raw width/scalar/node/provenance tamper checks. Not provider authentication or world13 admission.\n");exit=0;
   }catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(exit);
  }
 }
}
