using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EarthUnscripted.Updates
{
    public static class UpdateTransport
    {
        static readonly HttpClient Client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
        public static async Task<Tuple<byte[],string>> Feed(string etag,CancellationToken cancel)
        {
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                using(var request=new HttpRequestMessage(HttpMethod.Get,"https://earthunscripted.com/updates/install/v1/beta.json"))
                {
                    if(!string.IsNullOrEmpty(etag))request.Headers.TryAddWithoutValidation("If-None-Match",etag);
                    using(var response=await Client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false))
                    {
                        if(response.StatusCode==HttpStatusCode.NotModified)return Tuple.Create<byte[],string>(null,etag);
                        response.EnsureSuccessStatusCode();using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream())
                        {await Copy(input,output,UpdateProtocol.MaximumMetadata,null,timeout.Token).ConfigureAwait(false);return Tuple.Create(output.ToArray(),response.Headers.ETag?.ToString());}
                    }
                }
            }
        }
        public static async Task Download(JObject artifact,string path,Action<long,long> progress,CancellationToken cancel)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if(File.Exists(path)){try{UpdateProtocol.VerifyFile(path,artifact);progress?.Invoke((long)artifact["bytes"],(long)artifact["bytes"]);return;}catch(InvalidDataException){File.Delete(path);}}
            string temporary=path+".partial";Uri url=UpdateProtocol.ApprovedUrl((string)artifact["url"]);
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(30));
                try
                {
                    for(int redirects=0;;redirects++)
                    {
                        using(var request=new HttpRequestMessage(HttpMethod.Get,url))
                        using(var response=await Client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false))
                        {
                            int status=(int)response.StatusCode;
                            if(status>=300&&status<400){UpdateProtocol.Require(redirects<5&&response.Headers.Location!=null,"Too many download redirects.");url=UpdateProtocol.ApprovedUrl(new Uri(url,response.Headers.Location).AbsoluteUri,true);continue;}
                            response.EnsureSuccessStatusCode();long expected=(long)artifact["bytes"];
                            if(response.Content.Headers.ContentLength.HasValue)UpdateProtocol.Require(response.Content.Headers.ContentLength.Value==expected,"Download size does not match release metadata.");
                            using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,65536,true))
                            {await Copy(input,output,expected,bytes=>progress?.Invoke(bytes,expected),timeout.Token).ConfigureAwait(false);output.Flush(true);}
                            UpdateProtocol.VerifyFile(temporary,artifact);File.Move(temporary,path);return;
                        }
                    }
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
        }
        static async Task Copy(Stream input,Stream output,long limit,Action<long> progress,CancellationToken cancel)
        {
            var buffer=new byte[65536];long total=0;int count;
            while((count=await input.ReadAsync(buffer,0,buffer.Length,cancel).ConfigureAwait(false))>0)
            {total=checked(total+count);UpdateProtocol.Require(total<=limit,"Download exceeded its declared size.");await output.WriteAsync(buffer,0,count,cancel).ConfigureAwait(false);progress?.Invoke(total);}
        }
    }
    public sealed class RadioDelta
    {
        public long ReusedBytes,DownloadedBytes;
        sealed class Range {public string Path;public long Offset,Length;}
        readonly Dictionary<string,Range> available=new Dictionary<string,Range>();
        static ushort U16(byte[] b,int p)=>(ushort)(b[p]|b[p+1]<<8);
        static uint U32(byte[] b,int p)=>(uint)(b[p]|b[p+1]<<8|b[p+2]<<16|b[p+3]<<24);
        public void IndexArchives(IEnumerable<string> archives)
        {
            foreach(string path in archives.Distinct(StringComparer.Ordinal))
            {
                try
                {
                    using(var stream=File.OpenRead(path))
                    {
                        int size=(int)Math.Min(stream.Length,65557);var tail=new byte[size];stream.Position=stream.Length-size;ReadExact(stream,tail,0,size);
                        int end=size-22;while(end>=0&&(U32(tail,end)!=0x06054b50||end+22+U16(tail,end+20)!=size))end--;
                        if(end<0)continue;int entries=U16(tail,end+10);long offset=U32(tail,end+16);if(entries==65535||offset==uint.MaxValue)continue;
                        stream.Position=offset;
                        for(int i=0;i<entries;i++)
                        {
                            var central=new byte[46];ReadExact(stream,central,0,central.Length);if(U32(central,0)!=0x02014b50)break;
                            long length=U32(central,20),localOffset=U32(central,42),next=stream.Position+U16(central,28)+U16(central,30)+U16(central,32);
                            if(length>0&&length<=64L*1024*1024&&localOffset<stream.Length-30)
                            {
                                stream.Position=localOffset;var header=new byte[30];ReadExact(stream,header,0,30);
                                if(U32(header,0)==0x04034b50)
                                {
                                    long body=localOffset+30+U16(header,26)+U16(header,28);
                                    if(body+length<=stream.Length){stream.Position=body;string hash=HashRange(stream,length);available[hash]=new Range{Path=path,Offset=body,Length=length};}
                                }
                            }
                            stream.Position=next;
                        }
                    }
                }
                catch(Exception e) when(e is IOException||e is InvalidDataException||e is ArgumentException) { /* Cache miss: fetch verified content instead. */ }
            }
        }
        public async Task Reconstruct(JObject artifact,JObject recipeDescriptor,string output,string cache,IEnumerable<string> archives,Action<string> status,CancellationToken cancel)
        {
            Directory.CreateDirectory(cache);IndexArchives(archives);
            string recipePath=Path.Combine(cache,(string)recipeDescriptor["sha256"]+".json");
            await UpdateTransport.Download(recipeDescriptor,recipePath,null,cancel).ConfigureAwait(false);
            var recipe=UpdateProtocol.Json(File.ReadAllBytes(recipePath));
            UpdateProtocol.Require((string)recipe["format"]=="EarthUnscriptedRadioDelta"&&(int?)recipe["schemaVersion"]==1&&(string)recipe["sha256"]==(string)artifact["sha256"]&&(long?)recipe["bytes"]==(long)artifact["bytes"],"Radio delta does not describe this pack.");
            var pieces=recipe["pieces"] as JArray;UpdateProtocol.Require(pieces!=null&&pieces.Count<=12000,"Invalid radio delta recipe.");
            string temp=output+".partial";long total=0;
            try
            {
                using(var target=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
                {
                    foreach(var piece in pieces.OfType<JObject>())
                    {
                        cancel.ThrowIfCancellationRequested();long length=(long)piece["bytes"];string hash=(string)piece["sha256"];
                        UpdateProtocol.Require(length>0&&length<=64L*1024*1024&&UpdateProtocol.HashText(hash),"Invalid radio content piece.");
                        total=checked(total+length);UpdateProtocol.Require(total<=(long)artifact["bytes"],"Radio delta exceeded pack size.");
                        if(piece["inline"]!=null)
                        {
                            UpdateProtocol.Require(length<=32768,"Inline radio metadata is too large.");byte[] bytes=Convert.FromBase64String((string)piece["inline"]);
                            UpdateProtocol.Require(bytes.Length==length&&UpdateProtocol.Hash(bytes)==hash,"Radio metadata integrity failed.");target.Write(bytes,0,bytes.Length);
                        }
                        else if(available.TryGetValue(hash,out var source)&&source.Length==length)
                        {using(var input=File.OpenRead(source.Path)){input.Position=source.Offset;CopyRange(input,target,length);}ReusedBytes+=length;}
                        else
                        {
                            UpdateProtocol.ValidateArtifact(piece,64L*1024*1024);string file=Path.Combine(cache,hash+".bin");
                            bool cached=File.Exists(file);
                            await UpdateTransport.Download(piece,file,(done,count)=>status?.Invoke("Radio: "+(ReusedBytes/1024/1024)+" MB reused · "+((DownloadedBytes+done)/1024/1024)+" MB downloaded"),cancel).ConfigureAwait(false);
                            using(var input=File.OpenRead(file))input.CopyTo(target,65536);
                            if(cached)ReusedBytes+=length;else DownloadedBytes+=length;
                        }
                    }
                    target.Flush(true);
                }
                UpdateProtocol.VerifyFile(temp,artifact);if(File.Exists(output))File.Delete(output);File.Move(temp,output);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        static void ReadExact(Stream stream,byte[] bytes,int offset,int count)
        {while(count>0){int n=stream.Read(bytes,offset,count);if(n==0)throw new EndOfStreamException();offset+=n;count-=n;}}
        static string HashRange(Stream source,long bytes)
        {using(var hash=SHA256.Create()){var buffer=new byte[65536];while(bytes>0){int n=source.Read(buffer,0,(int)Math.Min(buffer.Length,bytes));if(n==0)throw new EndOfStreamException();hash.TransformBlock(buffer,0,n,null,0);bytes-=n;}hash.TransformFinalBlock(Array.Empty<byte>(),0,0);return BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();}}
        static void CopyRange(Stream source,Stream target,long bytes)
        {var buffer=new byte[65536];while(bytes>0){int n=source.Read(buffer,0,(int)Math.Min(buffer.Length,bytes));if(n==0)throw new EndOfStreamException();target.Write(buffer,0,n);bytes-=n;}}
    }
}
