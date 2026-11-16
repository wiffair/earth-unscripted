using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Text;using System.Threading.Tasks;using Newtonsoft.Json;using UnityEditor;using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class CanonicalGeography13Checks
    {
        [Serializable]sealed class Vector{public string kind,bits,token;}
        [Serializable]sealed class Vectors{public string mode;public Vector[] vectors;}
        [Serializable]sealed class FloatBox{public float value;}
        [Serializable]sealed class DoubleBox{public double value;}
        [Serializable]sealed class FieldsBox{public float x;public double station;public string text;public string[] strings;[NonSerialized]public float transient=99;public int Getter=>88;}
        [Serializable]sealed class DateBox{public DateTime timestamp;}
        [Serializable]public sealed class Report{public string status,mode=CanonicalGeography13.Mode,vectorsSha;public int checks,vectors;public List<string> passed=new List<string>();}
        static void Check(Report r,bool value,string why){if(!value)throw new InvalidOperationException("CANONICAL13 FAIL "+why);r.checks++;r.passed.Add(why);}
        static void Reject(Report r,Action run,string why){try{run();}catch(Exception e)when(e is JsonException||e is InvalidDataException){Check(r,true,why);return;}throw new InvalidOperationException("CANONICAL13 accepted "+why);}
        public static Report Execute(string output)
        {
            if(Directory.Exists(output)||!Path.GetFullPath(output).StartsWith("/private/tmp/",StringComparison.Ordinal))throw new InvalidOperationException("Fresh owned /private/tmp output required");Directory.CreateDirectory(output);var report=new Report();
            try
            {
                byte[] input=File.ReadAllBytes("/private/tmp/rr019-canonical13/independent-vectors.json");string sha=World13DocumentCodec.Hash(input);Check(report,sha=="467330b0958cb4884072d3b24009c16f4be156f8a8f277d0cfffb244f8119d1c","independent Python exact-rational vector bytes pinned");report.vectorsSha=sha;var data=WorldFieldJson.Read<Vectors>(new UTF8Encoding(false,true).GetString(input));Check(report,data.mode==CanonicalGeography13.Mode&&data.vectors.Length==417,"417 targeted/random IEEE vectors loaded");report.vectors=data.vectors.Length;
                foreach(var v in data.vectors)
                {
                    string json="{\"value\":"+v.token+"}";
                    if(v.kind=="float")
                    {uint bits=uint.Parse(v.bits,NumberStyles.HexNumber,CultureInfo.InvariantCulture);float value=BitConverter.Int32BitsToSingle(unchecked((int)bits));Check(report,CanonicalGeography13.FloatToken(value)==v.token,"float exact token "+v.bits);Check(report,CanonicalGeography13.Write(new FloatBox{value=value})==json,"float DTO exact bytes "+v.bits);var parsed=CanonicalGeography13.ReadCanonical<FloatBox>(Encoding.UTF8.GetBytes(json));Check(report,unchecked((uint)BitConverter.SingleToInt32Bits(parsed.value))==bits,"float exact read bits "+v.bits);}
                    else
                    {ulong bits=ulong.Parse(v.bits,NumberStyles.HexNumber,CultureInfo.InvariantCulture);double value=BitConverter.Int64BitsToDouble(unchecked((long)bits));Check(report,CanonicalGeography13.DoubleToken(value)==v.token,"double exact token "+v.bits);Check(report,CanonicalGeography13.Write(new DoubleBox{value=value})==json,"double DTO exact bytes "+v.bits);var parsed=CanonicalGeography13.ReadCanonical<DoubleBox>(Encoding.UTF8.GetBytes(json));Check(report,unchecked((ulong)BitConverter.DoubleToInt64Bits(parsed.value))==bits,"double exact read bits "+v.bits);}
                }
                foreach(float f in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})Reject(report,()=>CanonicalGeography13.Write(new FloatBox{value=f}),"reject nonfinite float");
                foreach(double d in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})Reject(report,()=>CanonicalGeography13.Write(new DoubleBox{value=d}),"reject nonfinite double");
                var fields=new FieldsBox{x=1.5f,station=BitConverter.Int64BitsToDouble(0x408f400000000001),text="2026-11-03T21:24:23Z",strings=new[]{"Cymraeg",""}};string canonical=CanonicalGeography13.Write(fields);Check(report,!canonical.Contains("Getter")&&!canonical.Contains("transient")&&canonical.IndexOf("station",StringComparison.Ordinal)<canonical.IndexOf("strings",StringComparison.Ordinal),"field-only/NonSerialized/ordinal contract retained");
                var clone=CanonicalGeography13.ReadCanonical<FieldsBox>(Encoding.UTF8.GetBytes(canonical));Check(report,BitConverter.DoubleToInt64Bits(clone.station)==0x408f400000000001&&clone.text=="2026-11-03T21:24:23Z","nextafter station and exact ISO string preserved");
                Reject(report,()=>CanonicalGeography13.ReadCanonical<FloatBox>(Encoding.UTF8.GetBytes("{\"value\":1.5000}")),"semantically equal noncanonical numeric spelling rejected");
                Reject(report,()=>CanonicalGeography13.ReadBytes<FloatBox>(new byte[]{255}),"malformed UTF8 rejected");Reject(report,()=>CanonicalGeography13.Write(new FieldsBox{text="\ud800"}),"malformed UTF16 field string rejected");Reject(report,()=>CanonicalGeography13.Write(new FieldsBox{strings=new[]{"\ud800"}}),"malformed UTF16 array string rejected");
                Reject(report,()=>CanonicalGeography13.Write(new DateBox{timestamp=DateTime.UtcNow}),"implicit DateTime serialization rejected");
                Parallel.For(0,32,i=>{if(CanonicalGeography13.Write(fields)!=canonical)throw new InvalidOperationException("Concurrent canonical output mismatch");});Check(report,true,"parallel writers keep deterministic per-call state");
                File.WriteAllText(Path.Combine(output,"contract.json"),canonical);report.status="PASS";
            }
            catch(Exception e){report.status="FAIL";File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());throw;}
            finally{File.WriteAllText(Path.Combine(output,"receipt.json"),WorldFieldJson.Write(report,true));}return report;
        }
    }
    public static class CanonicalGeography13Verification
    {
        public static void Run(){int exit=1;string output="/private/tmp/rr019-canonical13-unity-"+DateTime.UtcNow.Ticks;try{if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())throw new InvalidOperationException("Isolated canonical helper proof only");var r=CanonicalGeography13Checks.Execute(output);Debug.Log("CANONICAL13 PASS "+r.checks+" "+output);exit=0;}catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(exit);}
    }
}
