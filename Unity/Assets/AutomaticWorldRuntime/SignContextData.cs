using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    [Serializable] public sealed class SignContextSpan {public float start,end;public string region;}
    [Serializable] public sealed class SignContextData
    {
        public int schema;public string geometrySha256;public Row[] features;
        [Serializable] public sealed class Row {public string id,lighting,zone,limitForward,limitBackward;public bool dual,national;public SignContextSpan[] contexts;}
        public static void Apply(NewbridgeWorld.Map map,byte[] data,string expectedHash,int expectedBytes,string geometryHash)
        {
            using(var sha=SHA256.Create())if(data.Length!=expectedBytes||BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant()!=expectedHash)throw new InvalidOperationException("Sign context hash mismatch");
            var payload=JsonUtility.FromJson<SignContextData>(Encoding.UTF8.GetString(data));
            if((payload.schema!=1&&payload.schema!=2)||payload.geometrySha256!=geometryHash||payload.features==null)throw new InvalidOperationException("Sign context does not belong to this prepared area");
            var byId=new Dictionary<string,NewbridgeWorld.Feature>();foreach(var f in map.features)byId[f.id]=f;
            foreach(var row in payload.features)
            {
                if(!byId.TryGetValue(row.id,out var f))continue;
                foreach(var span in row.contexts??Array.Empty<SignContextSpan>())if(float.IsNaN(span.start)||float.IsNaN(span.end)||float.IsInfinity(span.end)||span.start<0||span.end<span.start)throw new InvalidOperationException("Invalid sign context span");
                f.signLighting=row.lighting;f.signZone=row.zone;f.signLimitForward=row.limitForward;f.signLimitBackward=row.limitBackward;f.signDual=row.dual;f.signNational=row.national;f.signContexts=row.contexts;
            }
        }
    }
}
