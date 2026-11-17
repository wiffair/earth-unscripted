using System;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>
    /// IMGUI-compatible controls used by every in-game menu. Mouse/keyboard paths
    /// still use Unity GUI; controller actions use the same return value and owner
    /// callbacks, preserving confirmation, save, validation and cancellation rules.
    /// Only the DrivingUI drawing scope enables navigation registration.
    /// </summary>
    public static class ControllerMenuGUI
    {
        public static ControllerMenuNavigation Active { get; private set; }
        public static bool Enabled => Active != null;
        public sealed class Scope : IDisposable
        {
            readonly ControllerMenuNavigation previous;
            public Scope(ControllerMenuNavigation navigation, string page)
            { previous = Active; Active = navigation; navigation.Begin(page); }
            public void Dispose() { Active.End(); Active = previous; }
        }
        public static Scope Begin(ControllerMenuNavigation navigation, string page) => new Scope(navigation, page);

        public static bool Button(Rect rect, string label, GUIStyle style = null, string identity = null)
        {
            int id = Active?.Register(rect, label, ControllerMenuNavigation.ControlKind.Button,identity) ?? -1;
            bool clicked = style == null ? GUI.Button(rect, label) : GUI.Button(rect, label, style);
            if (clicked) Active?.MouseSelected(id);
            return clicked || (Active?.TakeActivation(id) ?? false);
        }
        public static bool Button(Rect rect, GUIContent content, GUIStyle style)
        {
            int id = Active?.Register(rect, content.text, ControllerMenuNavigation.ControlKind.Button) ?? -1;
            bool clicked = GUI.Button(rect, content, style);
            if (clicked) Active?.MouseSelected(id);
            return clicked || (Active?.TakeActivation(id) ?? false);
        }
        public static bool CheckboxClick(Rect rect, string label)
        {
            int id = Active?.Register(rect, label, ControllerMenuNavigation.ControlKind.Button) ?? -1;
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            if (clicked) Active?.MouseSelected(id);
            return clicked || (Active?.TakeActivation(id) ?? false);
        }
        public static string TextField(Rect rect, string value, int maximum = 4096)
        {
            int id = Active?.Register(rect, "Edit text", ControllerMenuNavigation.ControlKind.Text) ?? -1;
            string edited = GUI.TextField(rect, value, maximum);
            return Active != null ? Active.TextValue(id, edited, maximum) : edited;
        }
        public static int Toolbar(Rect rect, int selected, string[] labels, GUIStyle style = null)
        { return SelectionGrid(rect, selected, labels, labels.Length, style); }
        public static int SelectionGrid(Rect rect, int selected, string[] labels, int columns, GUIStyle style = null)
        {
            int id = Active?.Register(rect, "Choose " + string.Join(" / ", labels), ControllerMenuNavigation.ControlKind.Choice) ?? -1;
            int result = GUI.SelectionGrid(rect, selected, labels, columns, style ?? GUI.skin.button);
            int change = Active?.TakeAdjustment(id) ?? 0;
            if (Active?.TakeActivation(id) == true) change = 1;
            if (change != 0 && labels.Length > 0) result = (selected + change + labels.Length) % labels.Length;
            if (result != selected && change == 0) Active?.MouseSelected(id);
            return result;
        }
        public static float HorizontalSlider(Rect rect, float value, float minimum, float maximum, string label = "Adjust value")
        {
            int id = Active?.Register(rect, label, ControllerMenuNavigation.ControlKind.Slider) ?? -1;
            var track=new Rect(rect.x+6,rect.center.y-1,Mathf.Max(0,rect.width-12),2);
            InterfaceTheme.Fill(track,new Color(.18f,.25f,.30f));
            InterfaceTheme.Fill(new Rect(track.x,track.y,track.width*Mathf.InverseLerp(minimum,maximum,value),2),InterfaceTheme.Gold);
            float result = GUI.HorizontalSlider(rect, value, minimum, maximum);
            int change = Active?.TakeAdjustment(id) ?? 0;
            Active?.TakeActivation(id);
            return change == 0 ? result : Mathf.Clamp(value + (maximum - minimum) * .025f * change, minimum, maximum);
        }
        public static bool Toggle(Rect rect, bool value, string label, GUIStyle style = null)
        {
            int id = Active?.Register(rect, label, ControllerMenuNavigation.ControlKind.Button) ?? -1;
            bool result = GUI.Toggle(rect, value, label, style ?? GUI.skin.toggle);
            if (Active?.TakeActivation(id) == true) result = !value;
            return result;
        }
        public static Vector2 BeginScrollView(Rect rect, Vector2 position, Rect content)
        { return GUI.BeginScrollView(rect, Active != null ? Active.BeginScroll(rect, position, content) : position, content); }
        public static void EndScrollView() { GUI.EndScrollView(); Active?.EndScroll(); }

        /// <summary>Focused maps pan with either stick, zoom with triggers, and select with A.</summary>
        public static bool Map(Rect rect, string label, out Vector2 movement, out float zoom)
        {
            movement = Vector2.zero; zoom = 0;
            if (Active == null) return false;
            int id = Active.Register(rect, label, ControllerMenuNavigation.ControlKind.Map);
            Active.MapMotion(id, out movement, out zoom);
            return Active.TakeActivation(id);
        }
    }
}
