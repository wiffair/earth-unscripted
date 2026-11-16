using System;
using System.Runtime.InteropServices;
using System.Text;
namespace RoadRush
{
    // A narrow SQLite adapter. Queries are fixed SQL with bound data; no extension loading.
    public sealed class NativeMapDatabase : IDisposable
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        const string Library="sqlite3";
#else
        const string Library="/usr/lib/libsqlite3.dylib";
#endif
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_busy_timeout(IntPtr db,int milliseconds);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int bytes,out IntPtr statement,IntPtr tail);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr statement);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_double(IntPtr statement,int index,double value);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_int64(IntPtr statement,int index,long value);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_text(IntPtr statement,int index,byte[] value,int bytes,IntPtr destructor);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_blob(IntPtr statement,int index,byte[] value,int bytes,IntPtr destructor);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern long sqlite3_column_int64(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern double sqlite3_column_double(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_blob(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_text(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_bytes(IntPtr statement,int index);
        IntPtr handle;static byte[] Utf8(string value)=>Encoding.UTF8.GetBytes(value+"\0");
        string Error=>Marshal.PtrToStringAnsi(sqlite3_errmsg(handle));
        public NativeMapDatabase(string path,bool writable=false)
        {
            // macOS system SQLite cannot open a copied/checkpointed WAL database
            // with READONLY alone when its auxiliary files do not yet exist.
            // Allow SQLite to maintain those files, then forbid all SQL writes
            // on ordinary readers. READWRITE does not include CREATE.
            int result=sqlite3_open_v2(Utf8(path),out handle,2,IntPtr.Zero);
            if(result!=0&&!writable){Dispose();result=sqlite3_open_v2(Utf8(path),out handle,1,IntPtr.Zero);}
            if(result!=0){string error=Error;Dispose();throw new InvalidOperationException(error);}sqlite3_busy_timeout(handle,1200);Execute("PRAGMA trusted_schema=OFF");if(!writable)Execute("PRAGMA query_only=ON");
        }
        public Statement Prepare(string sql){int code=sqlite3_prepare_v2(handle,Utf8(sql),-1,out var statement,IntPtr.Zero);if(code!=0)throw new InvalidOperationException(Error);return new Statement(this,statement);}
        public void Execute(string sql){using(var statement=Prepare(sql))while(statement.Step()){};}
        public void Dispose(){if(handle==IntPtr.Zero)return;sqlite3_close_v2(handle);handle=IntPtr.Zero;}
        public sealed class Statement : IDisposable
        {
            readonly NativeMapDatabase owner;IntPtr handle;
            internal Statement(NativeMapDatabase db,IntPtr statement){owner=db;handle=statement;}
            void Bound(int code){if(code!=0)throw new InvalidOperationException(owner.Error);}
            public Statement Number(int index,double value){Bound(sqlite3_bind_double(handle,index,value));return this;}
            public Statement Integer(int index,long value){Bound(sqlite3_bind_int64(handle,index,value));return this;}
            public Statement Text(int index,string value){var data=Encoding.UTF8.GetBytes(value);Bound(sqlite3_bind_text(handle,index,data,data.Length,new IntPtr(-1)));return this;}
            public Statement Bytes(int index,byte[] value){Bound(sqlite3_bind_blob(handle,index,value,value.Length,new IntPtr(-1)));return this;}
            public bool Step(){int code=sqlite3_step(handle);if(code==100)return true;if(code==101)return false;throw new InvalidOperationException(owner.Error);}
            public long Integer(int index)=>sqlite3_column_int64(handle,index);
            public double Number(int index)=>sqlite3_column_double(handle,index);
            public byte[] Bytes(int index){int length=sqlite3_column_bytes(handle,index);if(length<0||length>16000000)throw new InvalidOperationException("Oversized map field");var bytes=new byte[length];if(length>0)Marshal.Copy(sqlite3_column_blob(handle,index),bytes,0,length);return bytes;}
            public string Text(int index){int length=sqlite3_column_bytes(handle,index);if(length<0||length>1000000)throw new InvalidOperationException("Oversized map text");var bytes=new byte[length];if(length>0)Marshal.Copy(sqlite3_column_text(handle,index),bytes,0,length);return Encoding.UTF8.GetString(bytes);}
            public void Dispose(){if(handle==IntPtr.Zero)return;sqlite3_finalize(handle);handle=IntPtr.Zero;}
        }
    }
}
