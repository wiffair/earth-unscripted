using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Linq;
using Clipper2Lib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
    // Offline prepared-file diagnostic; never attached to an owner session.
    public static class AreaAdmissionProfileVerification
    {
        public static void CompareBatch()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();
                string Arg(string key){int at=Array.IndexOf(args,key);if(at<0||at+1>=args.Length)throw new ArgumentException(key);return Path.GetFullPath(args[at+1]);}
                PathsD[] Read(string path)
                {
                    var layers=new[]{new PathsD(),new PathsD()};
                    using(var stream=File.OpenRead(path))using(var reader=new BinaryReader(stream))
                    {int count=reader.ReadInt32();if(count<0||count>1000000)throw new InvalidDataException("Paving patch limit");
                     for(int n=0;n<count;n++){int layer=reader.ReadBoolean()?1:0,points=reader.ReadInt32();if(points<3||points>20000)throw new InvalidDataException("Paving vertex limit");var outline=new PathD();for(int i=0;i<points;i++)outline.Add(new PointD(reader.ReadDouble(),reader.ReadDouble()));layers[layer].Add(outline);}
                     if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing paving bytes");}
                    return layers;
                }
                string output=Arg("-earthPavingComparisonOutput");if(File.Exists(output))throw new IOException("Keep existing comparison evidence");
                var reference=Read(Arg("-earthPavingReference"));var candidate=Read(Arg("-earthPavingCandidate"));var rows=new JArray();bool within5mm=true;
                for(int layer=0;layer<2;layer++)
                {
                    var before=Clipper.Union(reference[layer],new PathsD(),FillRule.NonZero,3);var after=Clipper.Union(candidate[layer],new PathsD(),FillRule.NonZero,3);
                    var delta=Clipper.Xor(before,after,FillRule.NonZero,3);
                    // Source clipping is millimetre precision, followed by float
                    // world coordinates (~2mm ULP in this real fixture). This
                    // comparison never changes production clearance tolerances.
                    var beforeExpanded=Clipper.InflatePaths(before,.005,JoinType.Round,EndType.Polygon,2,4);
                    var afterExpanded=Clipper.InflatePaths(after,.005,JoinType.Round,EndType.Polygon,2,4);
                    var removedBeyond=Clipper.Difference(before,afterExpanded,FillRule.NonZero,4);var addedBeyond=Clipper.Difference(after,beforeExpanded,FillRule.NonZero,4);
                    double deltaArea=Math.Abs(Clipper.Area(delta)),outside=Math.Abs(Clipper.Area(removedBeyond))+Math.Abs(Clipper.Area(addedBeyond));
                    var eroded=Clipper.InflatePaths(delta,-.0025,JoinType.Round,EndType.Polygon,2,4);
                    double erodedArea=Math.Abs(Clipper.Area(eroded));
                    rows.Add(new JObject{{"island",layer==1},{"referenceTriangles",reference[layer].Count},{"candidateTriangles",candidate[layer].Count},{"referenceUnionAreaSquareMetres",Clipper.Area(before)},{"candidateUnionAreaSquareMetres",Clipper.Area(after)},{"symmetricDifferenceAreaSquareMetres",deltaArea},{"differenceBeyond5mmSquareMetres",outside},{"differenceRemainingAfter2_5mmErosionSquareMetres",erodedArea}});
                    if(outside>.000001||deltaArea>.02)within5mm=false;
                }
                File.WriteAllText(output,new JObject{{"passed",within5mm},{"within5mmHausdorffComparison",within5mm},{"layers",rows}}.ToString());
                UnityEngine.Debug.Log("PAVING SURFACE DIAGNOSTIC COMPLETE; original 5mm comparison="+within5mm);EditorApplication.Exit(within5mm?0:1);
            }
            catch(Exception error){UnityEngine.Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Batch()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();
                string Arg(string key){int at=Array.IndexOf(args,key);if(at<0||at+1>=args.Length)throw new ArgumentException(key);return Path.GetFullPath(args[at+1]);}
                string input=Arg("-earthAreaProfileInput"),output=Arg("-earthAreaProfileOutput");
                if(File.Exists(output)||File.Exists(Path.ChangeExtension(output,"paving.bin")))throw new IOException("Keep existing diagnostic evidence intact");
                int passes=3;int countAt=Array.IndexOf(args,"-earthAreaProfilePasses");
                if(countAt>=0&&(countAt+1>=args.Length||!int.TryParse(args[countAt+1],out passes)||passes<1||passes>5))throw new ArgumentException("Profile pass count must be 1 to 5");
                var rows=new JArray();
                for(int pass=0;pass<passes;pass++)
                {
                    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                    var watch=Stopwatch.StartNew();var source=JsonUtility.FromJson<NewbridgeWorld.Map>(File.ReadAllText(input));
                    double parse=watch.Elapsed.TotalMilliseconds;watch.Restart();
                    source.preparedPaving=NewbridgeWorld.PlanJunctionPaving(source);
                    double paving=watch.Elapsed.TotalMilliseconds;
                    if(pass==0)using(var stream=File.Create(Path.ChangeExtension(output,"paving.bin")))using(var writer=new BinaryWriter(stream))
                    {writer.Write(source.preparedPaving.Count);foreach(var patch in source.preparedPaving){writer.Write(patch.island);writer.Write(patch.outline.Count);foreach(var point in patch.outline){writer.Write(point.x);writer.Write(point.y);}}}
                    watch.Restart();
                    var index=typeof(NewbridgeWorld).GetMethod("BuildSpatialIndex",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
                    double indexing=watch.Elapsed.TotalMilliseconds;
                    string digest;
                    using(var buffer=new MemoryStream())
                    {using(var writer=new BinaryWriter(buffer,System.Text.Encoding.UTF8,true))foreach(var patch in source.preparedPaving){writer.Write(patch.island);writer.Write(patch.outline.Count);foreach(var v in patch.outline){writer.Write(v.x);writer.Write(v.y);}}
                     using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(buffer.ToArray())).Replace("-","").ToLowerInvariant();}
                    rows.Add(new JObject{{"parseMilliseconds",parse},{"junctionPavingMilliseconds",paving},{"spatialIndexAndJoinsMilliseconds",indexing},{"pavingPatchCount",source.preparedPaving.Count},{"pavingOutlineSha256",digest},{"features",source.features.Length},{"buildings",source.buildings.Length},{"points",source.features.Sum(f=>(long)f.points.Length)}});
                    GC.KeepAlive(index);
                }
                var report=new JObject{{"inputBytes",new FileInfo(input).Length},{"passes",rows}};
                File.WriteAllText(output,report.ToString());UnityEngine.Debug.Log("AREA ADMISSION PROFILE COMPLETE "+rows.Count+" passes");EditorApplication.Exit(0);
            }
            catch(Exception e){UnityEngine.Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
