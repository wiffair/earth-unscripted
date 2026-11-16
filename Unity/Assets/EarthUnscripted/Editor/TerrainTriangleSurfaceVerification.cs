using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.Editor.VerificationSupport;

namespace EarthUnscripted.Editor
{
    public static class TerrainTriangleSurfaceVerification
    {
        static string Hash(string path){if(!File.Exists(path))return "missing";using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path)));}
        public static void Run()
        {
            int exit=1;string output=Path.GetFullPath("Builds/Verification/TerrainTriangleSurface/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);
            string career=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json"),before=Hash(career);
            try
            {
                if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())throw new InvalidOperationException("Isolated numeric proof only");
                var report=TerrainTriangleSurfaceChecks.Execute(output);
                if(before!=Hash(career))throw new InvalidOperationException("Protected real career changed");
                Debug.Log("FINAL TERRAIN TRIANGLES PASS "+report.checks+" "+output);exit=0;
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);}EditorApplication.Exit(exit);
        }
    }
}
