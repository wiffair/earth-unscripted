using System;
using UnityEngine;

namespace EarthUnscripted
{
    public static class InterfaceFontSelection
    {
        static bool enumerated;
        static string[] installed;
        static readonly string[] WindowsBody = { "Segoe UI", "Arial", "Liberation Sans", "DejaVu Sans", "Noto Sans" };
        static readonly string[] MacBody = { "Helvetica Neue", "Arial", "Liberation Sans", "DejaVu Sans", "Noto Sans" };
        static readonly string[] OtherBody = { "Liberation Sans", "DejaVu Sans", "Noto Sans", "Arial" };
        static readonly string[] WindowsDisplay = { "Georgia", "Times New Roman", "Liberation Serif", "DejaVu Serif", "Noto Serif" };
        static readonly string[] MacDisplay = { "Georgia", "Times New Roman", "Liberation Serif", "DejaVu Serif", "Noto Serif" };
        static readonly string[] OtherDisplay = { "Liberation Serif", "DejaVu Serif", "Noto Serif", "Georgia", "Times New Roman" };

        // Pure selection: retain the OS-reported spelling and never nominate an
        // unavailable first family. TextCore must be able to load fontNames[0].
        public static string SelectFamily(string[] available, RuntimePlatform platform, bool display)
        {
            if (available == null || available.Length == 0) return null;
            bool windows = platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor;
            bool mac = platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor;
            var preferred = display ? (windows ? WindowsDisplay : mac ? MacDisplay : OtherDisplay)
                : (windows ? WindowsBody : mac ? MacBody : OtherBody);
            foreach (string family in preferred)
                foreach (string actual in available)
                    if (string.Equals(actual, family, StringComparison.OrdinalIgnoreCase)) return actual;
            return null;
        }

        static string[] Installed()
        {
            if (enumerated) return installed;
            enumerated = true;
            try { installed = Font.GetOSInstalledFontNames(); }
            catch (Exception error)
            { Debug.LogWarning("[InterfaceFont] OS font enumeration failed; using fallback (" + error.GetType().Name + ")."); }
            return installed;
        }

        // Caller caches the result. An unavailable display face uses the body
        // font; only the body falls back to Unity's built-in runtime font.
        public static Font Create(bool display, int size, Font fallback = null)
        {
            string family = SelectFamily(Installed(), Application.platform, display);
            Font candidate = null;
            if (family != null)
            {
                try
                {
                    candidate = Font.CreateDynamicFontFromOSFont(family, size);
                    var names = candidate ? candidate.fontNames : null;
                    if (candidate && candidate.dynamic && names != null && names.Length == 1
                        && string.Equals(names[0], family, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log("[InterfaceFont] " + (display ? "display" : "body") + " family=" + family);
                        return candidate;
                    }
                    Debug.LogWarning("[InterfaceFont] Selected OS family did not produce a matching dynamic font; using fallback.");
                }
                catch (Exception error)
                { Debug.LogWarning("[InterfaceFont] OS font creation failed; using fallback (" + error.GetType().Name + ")."); }
                if (candidate)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(candidate);
                    else UnityEngine.Object.DestroyImmediate(candidate);
                }
            }
            if (fallback)
            {
                Debug.Log("[InterfaceFont] display uses body fallback.");
                return fallback;
            }
            try
            {
                var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (builtin) Debug.Log("[InterfaceFont] body uses built-in LegacyRuntime.ttf fallback.");
                else Debug.LogError("[InterfaceFont] Built-in runtime font returned null.");
                return builtin;
            }
            catch (Exception error)
            {
                Debug.LogError("[InterfaceFont] Built-in runtime font unavailable (" + error.GetType().Name + ").");
                return null;
            }
        }
    }
}
