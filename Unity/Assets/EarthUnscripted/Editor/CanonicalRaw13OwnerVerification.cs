using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class CanonicalRaw13OwnerChecks
    {
        [Serializable]public sealed class Report{public string status,bodySHA256,scope="Fixed reviewed canonical RAW ownership only; no cache/world/network/prepared admission";public int checks;}
        static void Check(Report r,bool ok,string why){if(!ok)throw new InvalidOperationException("Canonical raw owner: "+why);r.checks++;}
        static void Reject(Report r,Action action,string why){try{action();}catch(Exception e)when(e is InvalidDataException||e is InvalidOperationException||e is ArgumentException||e is OperationCanceledException){Check(r,true,why);return;}throw new InvalidOperationException("Accepted "+why);}
        static VerifiedRawWorld13Document PrivateOwner(CanonicalRaw13Document owner)=>(VerifiedRawWorld13Document)typeof(CanonicalRaw13Document).GetField("legacy",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
        static World13Document Document(VerifiedRawWorld13Document owner)=>(World13Document)typeof(VerifiedRawWorld13Document).GetField("Document",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
        public static Report Execute(string output)
        {
            if(Directory.Exists(output)||!Path.GetFullPath(output).StartsWith("/private/tmp/",StringComparison.Ordinal))throw new InvalidOperationException("Fresh owned outside proof required");Directory.CreateDirectory(output);var r=new Report();
            try
            {
                using var cts=new CancellationTokenSource();cts.Cancel();
                Reject(r,()=>ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot("/private/tmp/does-not-exist",cts.Token),"cancel before source read");
                var input=ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot("/private/tmp/rr019-integration-drafts/ActualPantside");byte[] bytes=input.CopyDocumentBytes();r.bodySHA256=World13DocumentCodec.Hash(bytes);
                Check(r,r.bodySHA256==ReviewedCanonicalRaw13Input.ExpectedBodySHA256,"independent exact body pin");
                Check(r,input.CanonicalMode=="ieee-exact-decimal-v1"&&input.ConversionRecipe=="original-ways-grid-binary32-canonical13-v2","externally selected typed mode/recipe");
                Check(r,typeof(ReviewedCanonicalRaw13Input).GetConstructors().Length==0,"no public caller receipt/hash capability constructor");
                byte[] edited=(byte[])bytes.Clone();edited[0]=(byte)'[';Reject(r,()=>CanonicalRaw13Codec.Read(input,edited),"changed body rejected before ownership");
                Check(r,input.CopyDocumentBytes().SequenceEqual(bytes),"input owns copied source bytes");Reject(r,()=>CanonicalRaw13Codec.Read(null,bytes),"no payload-only trust upgrade");Reject(r,()=>CanonicalRaw13Codec.Read(input,bytes,cts.Token),"cancelled read");
                var owner=CanonicalRaw13Codec.Read(input,bytes);Check(r,!owner.PreparedAdmitted&&owner.Family=="Raw13Canonical-v1","raw family distinct from prepared/legacy families");
                Check(r,owner.Write().SequenceEqual(bytes),"new canonical writer exact bytes");byte[] copy=owner.Write();copy[0]=0;Check(r,owner.Write().SequenceEqual(bytes),"writer returns owned copy");
                Reject(r,()=>owner.Write(cts.Token),"cancelled writer");Reject(r,()=>owner.TakeOwnedDocument(cts.Token),"cancelled take");Check(r,owner.Write().SequenceEqual(bytes),"cancellation leaves owner usable");
                var legacy=PrivateOwner(owner);byte[] old=World13DocumentCodec.WriteVerifiedRaw(legacy);Check(r,World13DocumentCodec.Hash(old)!=ReviewedCanonicalRaw13Input.ExpectedBodySHA256,"legacy canonical bytes cannot substitute for exact family");
                Check(r,CanonicalGeography13.WriteBytes(WorldFieldJson.Read<World13Document>(System.Text.Encoding.UTF8.GetString(old))).SequenceEqual(bytes),"legacy validator differs only in representation for this body");
                string original=Document(legacy).geography.name;Document(legacy).geography.name=original+" changed";Reject(r,()=>owner.Write(),"serialized mutation rejected");Document(legacy).geography.name=original;
                string digest=Document(legacy).geography.roadSourceDigest;Document(legacy).geography.roadSourceDigest=new string('0',64);Reject(r,()=>owner.TakeOwnedDocument(),"nonserialized source metadata mutation rejected before take");Document(legacy).geography.roadSourceDigest=digest;
                string node=Document(legacy).geography.roads[0].sourceNodeIds[0];Document(legacy).geography.roads[0].sourceNodeIds[0]="wrong-source";Reject(r,()=>owner.Write(),"nonserialized node metadata mutation rejected");Document(legacy).geography.roads[0].sourceNodeIds[0]=node;
                Document(legacy).roadAuthority.schema=1;Reject(r,()=>owner.Write(),"prepared mutation cannot upgrade raw phase");Document(legacy).roadAuthority.schema=0;
                Parallel.For(0,4,i=>{if(!owner.Write().SequenceEqual(bytes))throw new InvalidOperationException("Parallel writer drift");});Check(r,true,"concurrent writers serialize owned state");
                using(var queuedCancel=new CancellationTokenSource())using(var ready=new ManualResetEventSlim())
                {
                    object sync=typeof(VerifiedRawWorld13Document).GetField("Sync",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(legacy);Task<bool> pending;
                    Monitor.Enter(sync);try{pending=Task.Run(()=>{ready.Set();try{owner.TakeOwnedDocument(queuedCancel.Token);return false;}catch(OperationCanceledException){return true;}});if(!ready.Wait(TimeSpan.FromSeconds(10)))throw new InvalidOperationException("Queued take did not start");queuedCancel.Cancel();}finally{Monitor.Exit(sync);}
                    Check(r,pending.GetAwaiter().GetResult(),"take cancelled while queued behind owner lock");Check(r,owner.Write().SequenceEqual(bytes),"queued cancellation never transfers ownership");
                }
                var taken=owner.TakeOwnedDocument();Check(r,taken.roadAuthority.sourceDigest==ReviewedCanonicalRaw13Input.ExpectedSourceSHA256&&taken.geography.roads.Length==799,"one-time raw handoff preserves source/complete roads");
                Reject(r,()=>owner.Write(),"write after handoff");Reject(r,()=>owner.TakeOwnedDocument(),"second handoff");
                Check(r,input.CopyDocumentBytes().SequenceEqual(bytes),"handoff does not mutate producer capability");
                r.status="PASS";
            }
            catch(Exception e){r.status="FAIL";File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());throw;}
            finally{File.WriteAllText(Path.Combine(output,"receipt.json"),WorldFieldJson.Write(r,true));}return r;
        }
    }
    public static class CanonicalRaw13OwnerVerification
    {
        public static void Run(){int exit=1;string output="/private/tmp/rr019-canonical-owner-unity-"+DateTime.UtcNow.Ticks;try{if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())throw new InvalidOperationException("Isolated raw ownership proof only");var r=CanonicalRaw13OwnerChecks.Execute(output);Debug.Log("CANONICAL RAW OWNER PASS "+r.checks+" "+output);exit=0;}catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(exit);}
    }
}
