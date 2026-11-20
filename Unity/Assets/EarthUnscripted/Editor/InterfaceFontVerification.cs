using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    // Run explicitly with -executeMethod EarthUnscripted.Editor.InterfaceFontVerification.Run.
    // Selection fixtures plus local glyph generation; Windows IMGUI still needs a rendered check.
    public static class InterfaceFontVerification
    {
        static int checks;
        static void Check(bool okay, string message)
        {
            if (!okay) throw new Exception("INTERFACE FONT FAIL: " + message);
            checks++;
        }

        public static void Run()
        {
            checks = 0;
            var win = new[] { "Arial", "Segoe UI", "Georgia", "Times New Roman" };
            Check(InterfaceFontSelection.SelectFamily(win, RuntimePlatform.WindowsPlayer, false) == "Segoe UI",
                "Windows selects installed Segoe UI without unavailable Helvetica Neue");
            Check(InterfaceFontSelection.SelectFamily(new[] { "Helvetica Neue", "Arial", "Segoe UI" }, RuntimePlatform.WindowsEditor, false) == "Segoe UI",
                "Windows priority is explicit even when Helvetica is present");
            Check(InterfaceFontSelection.SelectFamily(new[] { "sEgOe uI", "Arial" }, RuntimePlatform.WindowsPlayer, false) == "sEgOe uI",
                "case insensitive lookup returns exact installed spelling");
            Check(InterfaceFontSelection.SelectFamily(new[] { "Arial" }, RuntimePlatform.WindowsPlayer, false) == "Arial", "Windows secondary candidate");
            Check(InterfaceFontSelection.SelectFamily(win, RuntimePlatform.WindowsPlayer, true) == "Georgia", "installed serif face");
            Check(InterfaceFontSelection.SelectFamily(new[] { "Arial" }, RuntimePlatform.WindowsPlayer, true) == null, "missing display face requests body fallback");
            Check(InterfaceFontSelection.SelectFamily(new[] { "Arial", "Helvetica Neue" }, RuntimePlatform.OSXPlayer, false) == "Helvetica Neue", "Mac appearance preserved");
            Check(InterfaceFontSelection.SelectFamily(new[] { "Noto Sans", "DejaVu Sans" }, RuntimePlatform.LinuxPlayer, false) == "DejaVu Sans", "Linux installed priority");
            Check(InterfaceFontSelection.SelectFamily(Array.Empty<string>(), RuntimePlatform.WindowsPlayer, false) == null, "empty enumeration requests built-in fallback");
            Check(InterfaceFontSelection.SelectFamily(null, RuntimePlatform.OSXPlayer, true) == null, "failed enumeration requests fallback");
            Check(InterfaceFontSelection.SelectFamily(new[] { "Unknown Font", null, "" }, RuntimePlatform.WindowsPlayer, false) == null, "unrecognized fonts never become first family");

            var messages = new List<string>();
            void Log(string message, string stack, LogType type)
            {
                if (message.Contains("Unable to load font face") || message.Contains("No Font Asset has been assigned")) messages.Add(message);
            }
            Font body = null, display = null;
            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Application.logMessageReceived += Log;
            try
            {
                Check(builtin && builtin.dynamic && builtin.HasCharacter('A'), "built-in runtime font has basic glyph data");
                body = InterfaceFontSelection.Create(false, 24);
                display = InterfaceFontSelection.Create(true, 66, body);
                Check(body && display, "current platform creation returns body and display fonts");
                Validate(body, builtin, false, 24);
                Validate(display, builtin, true, 66);
                Check(messages.Count == 0, "no reported face/mesh failures during local verification");
            }
            finally
            {
                Application.logMessageReceived -= Log;
                // These are separate test-created fonts. Never destroy the built-in
                // resource or initialize/change InterfaceTheme's cached live fonts.
                if (display && display != builtin && display != body) UnityEngine.Object.DestroyImmediate(display);
                if (body && body != builtin) UnityEngine.Object.DestroyImmediate(body);
            }
            Debug.Log("INTERFACE FONT PASS: " + checks + " checks; Windows rendered acceptance remains required.");
        }

        static void Validate(Font font, Font builtin, bool display, int size)
        {
            if (font == builtin) return; // Do not prewarm/mutate the shared built-in atlas.
            var available = Font.GetOSInstalledFontNames();
            string family = InterfaceFontSelection.SelectFamily(available, Application.platform, display);
            if (family == null && display) family = InterfaceFontSelection.SelectFamily(available, Application.platform, false);
            Check(font.fontNames.Length == 1 && string.Equals(font.fontNames[0], family, StringComparison.OrdinalIgnoreCase), "single installed primary family");
            const string sample = "Earth Unscripted New journey Settings ABC xyz 0123456789";
            font.RequestCharactersInTexture(sample, size, FontStyle.Normal);
            Check(font.material && font.material.mainTexture, "test font atlas exists after prewarm");
            using (var generator = new TextGenerator())
            {
                var settings = new TextGenerationSettings { font = font, fontSize = size, fontStyle = FontStyle.Normal,
                    color = Color.white, lineSpacing = 1, scaleFactor = 1, textAnchor = TextAnchor.UpperLeft,
                    generationExtents = new Vector2(4096, 256), horizontalOverflow = HorizontalWrapMode.Overflow,
                    verticalOverflow = VerticalWrapMode.Overflow, generateOutOfBounds = true };
                Check(generator.Populate(sample, settings) && generator.vertexCount > 0, "local sample produces glyph geometry");
            }
        }
    }
}
