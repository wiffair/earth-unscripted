using System;using System.IO;using System.Runtime.InteropServices;using System.Text;
namespace RoadRush.Editor
{
    // Generated fixture writer ONLY. No production-source paths are accepted.
    internal sealed class CoherentSqliteFixture:IDisposable
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        const string Library="sqlite3";
#else
        const string Library="/usr/lib/libsqlite3.dylib";
#endif
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_exec(IntPtr db,byte[] sql,IntPtr callback,IntPtr arg,out IntPtr error);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern void sqlite3_free(IntPtr ptr);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int length,out IntPtr statement,IntPtr tail);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_int64(IntPtr stmt,int index,long value);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_double(IntPtr stmt,int index,double value);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_text(IntPtr stmt,int index,byte[] value,int length,IntPtr destructor);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_blob(IntPtr stmt,int index,byte[] value,int length,IntPtr destructor);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr stmt);
        IntPtr db;public string Path{get;}
        static byte[] Z(string s)=>Encoding.UTF8.GetBytes(s+"\0");
        public CoherentSqliteFixture(string path)
        {Path=System.IO.Path.GetFullPath(path);if(!Path.StartsWith("/private/tmp/rr019-sqlite-proof-",StringComparison.Ordinal))throw new InvalidOperationException("Generated isolated fixture root only");if(sqlite3_open_v2(Z(Path),out db,2|4|0x10000,IntPtr.Zero)!=0)throw new InvalidOperationException("Fixture open failed");Exec("PRAGMA busy_timeout=100; PRAGMA foreign_keys=ON");}
        public void Exec(string sql){int code=sqlite3_exec(db,Z(sql),IntPtr.Zero,IntPtr.Zero,out var error);if(code!=0){string message=Marshal.PtrToStringAnsi(error);sqlite3_free(error);throw new InvalidOperationException("Fixture SQL "+code+": "+message);}}
        public void Run(string sql,params object[] values)
        {
            if(sqlite3_prepare_v2(db,Z(sql),-1,out var stmt,IntPtr.Zero)!=0)throw new InvalidOperationException("Fixture prepare");try{for(int i=0;i<values.Length;i++){object value=values[i];int code;if(value is byte[] blob)code=sqlite3_bind_blob(stmt,i+1,blob,blob.Length,new IntPtr(-1));else if(value is string s){byte[] b=Encoding.UTF8.GetBytes(s);code=sqlite3_bind_text(stmt,i+1,b,b.Length,new IntPtr(-1));}else if(value is double d)code=sqlite3_bind_double(stmt,i+1,d);else code=sqlite3_bind_int64(stmt,i+1,Convert.ToInt64(value));if(code!=0)throw new InvalidOperationException("Fixture bind");}if(sqlite3_step(stmt)!=101)throw new InvalidOperationException("Fixture write failed");}finally{sqlite3_finalize(stmt);}
        }
        public void Create()
        {
            Exec("PRAGMA journal_mode=WAL; CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT); CREATE TABLE features(id INTEGER PRIMARY KEY,kind INTEGER,tags TEXT,geometry BLOB); CREATE VIRTUAL TABLE bounds USING rtree(id,minLon,maxLon,minLat,maxLat); CREATE TABLE railways(id INTEGER PRIMARY KEY,tags TEXT,geometry BLOB); CREATE VIRTUAL TABLE rail_bounds USING rtree(id,minLon,maxLon,minLat,maxLat); CREATE TABLE nodes(id INTEGER PRIMARY KEY,lat INTEGER,lon INTEGER); CREATE TABLE usage(node,way,PRIMARY KEY(node,way)) WITHOUT ROWID;");
            foreach(var pair in new[]{new[]{"osmosis_replication_base_url","https://example.invalid/country-updates"},new[]{"osmosis_replication_sequence_number","1074"},new[]{"osmosis_replication_timestamp","2026-11-03T21:24:23Z"},new[]{"railway_schema","1"},new[]{"railway_complete","1"},new[]{"railway_source_sha256",new string('a',64)}})Run("INSERT INTO metadata VALUES(?1,?2)",pair[0],pair[1]);
        }
        public void Add(long id,int kind,double[] xs,double[] zs,bool rail=false,string tags=null)
        {
            const double lat=51.671156,lon=-3.131748;double cos=Math.Cos(lat*Math.PI/180);using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory))
            {
                writer.Write(xs.Length);int minLat=int.MaxValue,maxLat=int.MinValue,minLon=int.MaxValue,maxLon=int.MinValue;
                for(int i=0;i<xs.Length;i++){long node=id*1000+i;if(xs.Length>2&&i==xs.Length-1&&xs[0]==xs[i]&&zs[0]==zs[i])node=id*1000;int la=(int)Math.Round((lat+zs[i]/111319.4908)*1e7),lo=(int)Math.Round((lon+xs[i]/(111319.4908*cos))*1e7);writer.Write(node);writer.Write(la);writer.Write(lo);minLat=Math.Min(minLat,la);maxLat=Math.Max(maxLat,la);minLon=Math.Min(minLon,lo);maxLon=Math.Max(maxLon,lo);Run("INSERT OR IGNORE INTO nodes VALUES(?1,?2,?3)",node,la,lo);}
                string table=rail?"railways":"features",bounds=rail?"rail_bounds":"bounds";byte[] bytes=memory.ToArray();if(rail)Run("INSERT INTO railways VALUES(?1,?2,?3)",id,tags??"{\"highway\":\"residential\",\"railway\":\"tram\",\"name\":\"OLD\"}",bytes);else Run("INSERT INTO features VALUES(?1,?2,?3,?4)",id,kind,tags??(kind==2?"{\"building\":\"yes\"}":"{\"highway\":\"residential\",\"name\":\"OLD\"}"),bytes);Run("INSERT INTO "+bounds+" VALUES(?1,?2,?3,?4,?5)",id,minLon/1e7,maxLon/1e7,minLat/1e7,maxLat/1e7);
            }
        }
        public void Dispose(){if(db==IntPtr.Zero)return;sqlite3_close_v2(db);db=IntPtr.Zero;}
    }
}
