using System;using System.Diagnostics;using System.IO;using System.Runtime.InteropServices;using System.Text;using System.Threading;
namespace EarthUnscripted
{
    // Actual local SQLite, strict main-db READONLY. WAL readmarks/locking are
    // ordinary reader auxiliaries; zero auxiliary-byte mutation is NOT promised.
    public sealed class ReadOnlyCaptureSqlite:IDisposable
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        const string Library="sqlite3";
#else
        const string Library="/usr/lib/libsqlite3.dylib";
#endif
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Progress(IntPtr unused);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_db_readonly(IntPtr db,byte[] name);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_total_changes(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_busy_timeout(IntPtr db,int milliseconds);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern void sqlite3_progress_handler(IntPtr db,int operations,Progress progress,IntPtr data);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int bytes,out IntPtr statement,IntPtr tail);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_stmt_readonly(IntPtr statement);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr statement);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_double(IntPtr statement,int index,double value);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_int64(IntPtr statement,int index,long value);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_text(IntPtr statement,int index,byte[] value,int bytes,IntPtr destructor);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_type(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern long sqlite3_column_int64(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_blob(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_text(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_bytes(IntPtr statement,int index);
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);IntPtr handle;readonly CancellationToken token;readonly long deadline;readonly int thread;readonly Progress progress;bool transaction,deadlineHit;
        static byte[] Z(string value)=>Utf8.GetBytes(value+"\0");
        public bool MainReadOnly{get{Check();return sqlite3_db_readonly(handle,Z("main"))==1;}}
        public int TotalChanges{get{Check();return sqlite3_total_changes(handle);}}
        public ReadOnlyCaptureSqlite(string path,CancellationToken token,double timeoutSeconds=10)
        {
            if(double.IsNaN(timeoutSeconds)||double.IsInfinity(timeoutSeconds)||timeoutSeconds<=0||timeoutSeconds>60)throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));path=Path.GetFullPath(path);if(!File.Exists(path))throw new FileNotFoundException("Selected source database missing",path);this.token=token;thread=Environment.CurrentManagedThreadId;deadline=Stopwatch.GetTimestamp()+(long)(timeoutSeconds*Stopwatch.Frequency);progress=OnProgress;
            token.ThrowIfCancellationRequested();string uri="file:"+Uri.EscapeDataString(path).Replace("%2F","/")+"?mode=ro&cache=private";
            // READONLY|URI|FULLMUTEX|PRIVATECACHE. No READWRITE/CREATE fallback,
            // immutable switch, extension loading or writable attachment path.
            int code=sqlite3_open_v2(Z(uri),out handle,1|0x40|0x10000|0x40000,IntPtr.Zero);
            if(code!=0){string error=Error();Dispose();throw new InvalidDataException("Read-only SQLite open: "+error);}
            try{sqlite3_busy_timeout(handle,100);sqlite3_progress_handler(handle,1000,progress,IntPtr.Zero);if(!MainReadOnly)throw new InvalidDataException("SQLite main database is not read-only");Control("PRAGMA query_only=ON",true);Control("PRAGMA trusted_schema=OFF",true);}
            catch{Dispose();throw;}
        }
        string Error()=>handle==IntPtr.Zero?"closed":Marshal.PtrToStringAnsi(sqlite3_errmsg(handle));
        int OnProgress(IntPtr unused){if(token.IsCancellationRequested)return 1;if(Stopwatch.GetTimestamp()>=deadline){deadlineHit=true;return 1;}return 0;}
        void CheckOwner(){if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("SQLite capture connection is worker-owned");}
        internal void Check(){CheckOwner();token.ThrowIfCancellationRequested();if(deadlineHit||Stopwatch.GetTimestamp()>=deadline)throw new TimeoutException("Read-only source capture timed out");if(handle==IntPtr.Zero)throw new ObjectDisposedException(nameof(ReadOnlyCaptureSqlite));}
        internal void Fail(int code){if(token.IsCancellationRequested)throw new OperationCanceledException(token);if(deadlineHit)throw new TimeoutException("Read-only source query timed out");throw new InvalidDataException("Read-only SQLite error "+code+": "+Error());}
        void Control(string sql,bool check)
        {if(check)Check();int code=sqlite3_prepare_v2(handle,Z(sql),-1,out var stmt,IntPtr.Zero);if(code!=0)Fail(code);try{while((code=sqlite3_step(stmt))==100){}if(code!=101)Fail(code);}finally{sqlite3_finalize(stmt);}}
        public void BeginSnapshot(){Check();if(transaction)throw new InvalidOperationException("Snapshot already open");Control("BEGIN",true);transaction=true;}
        public void EndSnapshot(){CheckOwner();if(handle==IntPtr.Zero||!transaction)return;sqlite3_progress_handler(handle,0,null,IntPtr.Zero);try{Control("ROLLBACK",false);}finally{transaction=false;sqlite3_progress_handler(handle,1000,progress,IntPtr.Zero);}}
        public Statement Select(string sql)
        {
            Check();if(sql==null||sql.Length>4096||sql.Contains(";"))throw new InvalidDataException("One fixed read statement required");string command=sql.TrimStart();string first=new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.TakeWhile(command,c=>char.IsLetter(c))));if(!first.Equals("SELECT",StringComparison.OrdinalIgnoreCase)&&!first.Equals("WITH",StringComparison.OrdinalIgnoreCase)&&!command.Equals("PRAGMA query_only",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Source API permits SELECT/WITH reads and query_only diagnostic only");int code=sqlite3_prepare_v2(handle,Z(sql),-1,out var stmt,IntPtr.Zero);if(code!=0)Fail(code);if(stmt==IntPtr.Zero)throw new InvalidDataException("Empty source query");if(sqlite3_stmt_readonly(stmt)!=1){sqlite3_finalize(stmt);throw new InvalidDataException("Source statements must be read-only");}return new Statement(this,stmt);
        }
        public void Dispose(){CheckOwner();if(handle==IntPtr.Zero)return;try{EndSnapshot();}finally{sqlite3_progress_handler(handle,0,null,IntPtr.Zero);sqlite3_close_v2(handle);handle=IntPtr.Zero;GC.KeepAlive(progress);}}
        public sealed class Statement:IDisposable
        {
            readonly ReadOnlyCaptureSqlite owner;IntPtr statement;internal Statement(ReadOnlyCaptureSqlite owner,IntPtr statement){this.owner=owner;this.statement=statement;}
            void Check(){owner.Check();if(statement==IntPtr.Zero)throw new ObjectDisposedException(nameof(Statement));}
            public Statement Integer(int index,long value){Check();int code=sqlite3_bind_int64(statement,index,value);if(code!=0)owner.Fail(code);return this;}
            public Statement Number(int index,double value){Check();if(double.IsNaN(value)||double.IsInfinity(value))throw new InvalidDataException("Finite query coordinate required");int code=sqlite3_bind_double(statement,index,value);if(code!=0)owner.Fail(code);return this;}
            public Statement Text(int index,string value){Check();byte[] bytes=Utf8.GetBytes(value);if(bytes.Length>16384)throw new InvalidDataException("Source query parameter budget");int code=sqlite3_bind_text(statement,index,bytes,bytes.Length,new IntPtr(-1));if(code!=0)owner.Fail(code);return this;}
            public bool Step(){Check();int code=sqlite3_step(statement);if(code!=100&&code!=101)owner.Fail(code);Check();return code==100;}
            public bool IsNull(int index){Check();return sqlite3_column_type(statement,index)==5;}
            public long Integer(int index){Check();if(sqlite3_column_type(statement,index)!=1)throw new InvalidDataException("Exact nonnull SQLite INTEGER required");return sqlite3_column_int64(statement,index);}
            public byte[] Blob(int index,int maximum){Check();if(sqlite3_column_type(statement,index)!=4)throw new InvalidDataException("Exact nonnull SQLite BLOB required");int count=sqlite3_column_bytes(statement,index);if(count<0||count>maximum)throw new InvalidDataException("Bounded source BLOB exceeded");IntPtr ptr=sqlite3_column_blob(statement,index);if(ptr==IntPtr.Zero&&count!=0)throw new InvalidDataException("Missing source BLOB");byte[] result=new byte[count];if(count>0)Marshal.Copy(ptr,result,0,count);return result;}
            public string Text(int index,int maximum){Check();if(sqlite3_column_type(statement,index)!=3)throw new InvalidDataException("Exact nonnull SQLite TEXT required");IntPtr ptr=sqlite3_column_text(statement,index);int count=sqlite3_column_bytes(statement,index);if(count<0||count>maximum||ptr==IntPtr.Zero)throw new InvalidDataException("Bounded nonnull source text required");byte[] bytes=new byte[count];if(count>0)Marshal.Copy(ptr,bytes,0,count);try{return Utf8.GetString(bytes);}catch(DecoderFallbackException e){throw new InvalidDataException("Malformed source UTF8 text",e);}}
            public void Dispose(){owner.CheckOwner();if(statement==IntPtr.Zero)return;sqlite3_finalize(statement);statement=IntPtr.Zero;}
        }
    }
}
