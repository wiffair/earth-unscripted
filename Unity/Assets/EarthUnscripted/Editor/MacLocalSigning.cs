using System;
using System.IO;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    /// <summary>Local development signing, not Developer ID distribution or
    /// notarization. Verify nested Unity libraries before reporting a Mac build
    /// ready. This generated output bundle must live outside a cloud provider
    /// that continually reattaches Finder metadata.</summary>
    public static class MacLocalSigning
    {
        // ProcessStartInfo executes directly: no shell, environment expansion or
        // command substitution. Quote the complete app path as one argument.
        static string Quote(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
        static void Run(string executable,string arguments)
        {
            var start=new System.Diagnostics.ProcessStartInfo(executable,arguments)
                {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            using(var process=System.Diagnostics.Process.Start(start))
            {
                if(process==null)throw new IOException("Could not start local Mac signing verification");
                string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();process.WaitForExit();
                if(process.ExitCode!=0)throw new IOException("Local Mac signing check failed. Build outside cloud-synced folders. "+error+output);
            }
        }
        public static void SignAndVerify(string app)
        {
            string path=Path.GetFullPath(app);
            if(Application.platform!=RuntimePlatform.OSXEditor||!Directory.Exists(path)||!path.EndsWith(".app",StringComparison.Ordinal))throw new IOException("Existing local Mac app output required");
            // Strip inherited bundle-only resource metadata; never alter source
            // assets, user preferences, downloaded country caches or signing keys.
            Run("/usr/bin/xattr","-cr "+Quote(path));
            Run("/usr/bin/codesign","--force --deep --sign - --preserve-metadata=entitlements "+Quote(path));
            Run("/usr/bin/codesign","--verify --deep --strict "+Quote(path));
            Debug.Log("Earth Unscripted local ad-hoc Mac signature verified; not notarized");
        }
    }
}
