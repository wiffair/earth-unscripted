using System;
using System.Reflection;
using System.Reflection.Emit;
using UnityEditor;
using UnityEngine;
using World=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    // Private diagnostic draft. Execute only after the parent releases Unity.
    public static class NativeSegmentStrideProbe
    {
        public static int Measure()
        {
            var type=typeof(World).GetNestedType("Segment",BindingFlags.NonPublic);
            var method=new DynamicMethod("NativeSegmentManagedSize",typeof(int),Type.EmptyTypes,typeof(World),true);
            var il=method.GetILGenerator();il.Emit(OpCodes.Sizeof,type);il.Emit(OpCodes.Ret);
            return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
        }
        public static void Batch(){try{Debug.Log("NATIVE_SEGMENT_MANAGED_STRIDE "+Measure()+" pointerBytes="+IntPtr.Size);EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
