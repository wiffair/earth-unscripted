using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EarthUnscripted
{
    public enum MenuPulse { Move, Confirm, Back, Unavailable }

    /// <summary>
    /// Main-thread controller focus for the existing IMGUI menus. Repaint publishes
    /// the actual enabled controls, including scroll content; Update consumes one
    /// input edge. Commands are consumed once by the same control on the next draw.
    /// Screen rectangles are used only for spatial navigation, never game positions.
    /// </summary>
    public sealed class ControllerMenuNavigation
    {
        public enum ControlKind { Button, Choice, Slider, Text, Scroll, Map }
        public sealed class Control
        {
            public int Id, ScrollId = -1;
            public Rect Screen;
            public string Label;
            public ControlKind Kind;
        }

        const float RepeatDelaySeconds = .34f, RepeatIntervalSeconds = .12f;
        readonly List<Control> controls = new List<Control>();
        readonly List<Control> collecting = new List<Control>();
        readonly Stack<int> scrolls = new Stack<int>();
        readonly Dictionary<string,int> identities = new Dictionary<string,int>();
        readonly Dictionary<string, int> rememberedFocus = new Dictionary<string, int>();
        readonly Dictionary<int, int> scrolledFrames = new Dictionary<int, int>();
        string context = "", textTarget = "", completedTarget = "", completedText = "";
        string typed = "", textTitle = "";
        int sequence, focused, activate = -1, adjust, editLimit, heldDirection;
        float repeatAt;
        bool editing, upper;
        Vector2 scrollMotion, mapMotion;
        float zoomMotion;
        Action<MenuPulse> pulse;

        public bool Editing => editing;
        public bool ControllerActive { get; private set; }
        public IReadOnlyList<Control> Controls => controls;
        public Control Focused => controls.Find(control => control.Id == focused);
        public string Context => context;
        public void SetFeedback(Action<MenuPulse> feedback) { pulse = feedback; }
        void Feedback(MenuPulse value) { pulse?.Invoke(value); }

        internal void Begin(string page)
        {
            string next = editing ? page + "/keyboard" : page;
            if (next != context)
            {
                rememberedFocus[context] = focused;
                context = next;
                focused = rememberedFocus.TryGetValue(context, out int remembered) ? remembered : 0;
                controls.Clear(); activate = -1; adjust = 0;
                GUIUtility.keyboardControl = 0;
            }
            sequence = 0; scrolls.Clear();
            if (Event.current.type == EventType.Repaint) collecting.Clear();
        }

        internal void End()
        {
            if (Event.current.type != EventType.Repaint) return;
            controls.Clear(); controls.AddRange(collecting);
            activate = -1; adjust = 0;
            if (Focused == null && controls.Count > 0) focused = controls[0].Id;
        }

        internal int Register(Rect rect, string label, ControlKind kind, string identity = null)
        {
            int id = sequence++;
            if(identity!=null)
            {
                string key=context+"/"+identity;
                if(!identities.TryGetValue(key,out id)){id=-1000-identities.Count;identities.Add(key,id);}
            }
            if (GUI.enabled && Event.current.type == EventType.Repaint)
            {
                Vector2 position = GUIUtility.GUIToScreenPoint(rect.position);
                Vector2 end = GUIUtility.GUIToScreenPoint(rect.position + rect.size);
                collecting.Add(new Control { Id = id, Label = label ?? "", Kind = kind,
                    Screen = new Rect(position, end - position), ScrollId = scrolls.Count > 0 ? scrolls.Peek() : -1 });
            }
            if (GUI.enabled && ControllerActive && focused == id && Event.current.type == EventType.Repaint)
            {
                var old = GUI.color; GUI.color = InterfaceTheme.Gold;
                const float thickness = 2;
                GUI.DrawTexture(new Rect(rect.x - thickness, rect.y - thickness, rect.width + thickness * 2, thickness), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x - thickness, rect.yMax, rect.width + thickness * 2, thickness), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.xMax, rect.y, thickness, rect.height), Texture2D.whiteTexture);
                GUI.color = old;
            }
            return id;
        }

        internal bool TakeActivation(int id)
        {
            if (!GUI.enabled || activate != id) return false;
            activate = -1; return true;
        }
        internal int TakeAdjustment(int id)
        {
            if (!GUI.enabled || focused != id) return 0;
            int result = adjust; adjust = 0; return result;
        }
        internal void MouseSelected(int id)
        { focused = id; ControllerActive = false; Feedback(MenuPulse.Confirm); }

        /// <summary>Find a directional neighbour among real enabled controls.</summary>
        public static Control Neighbour(IReadOnlyList<Control> items, Control current, Vector2 direction)
        {
            Control best = null; float score = float.PositiveInfinity;
            if (current == null) return items.Count > 0 ? items[0] : null;
            // Scrolled list rows keep their displayed order even when the
            // next row is temporarily below the viewport and a footer is nearer.
            if(current.ScrollId>=0&&direction.x==0)
            {
                foreach(var candidate in items)
                {
                    if(candidate.Id==current.Id||candidate.ScrollId!=current.ScrollId)continue;
                    float forward=Vector2.Dot(candidate.Screen.center-current.Screen.center,direction);
                    float across=Mathf.Abs(candidate.Screen.center.x-current.Screen.center.x);
                    float candidateScore=forward+across*.8f;
                    if(forward>=1&&candidateScore<score){best=candidate;score=candidateScore;}
                }
                if(best!=null)return best;
            }
            foreach (var candidate in items)
            {
                if (candidate.Id == current.Id || candidate.Id == current.ScrollId) continue;
                if(candidate.Kind==ControlKind.Scroll)
                {
                    bool populated=false;foreach(var child in items)if(child.ScrollId==candidate.Id){populated=true;break;}
                    if(populated)continue;
                }
                var delta = candidate.Screen.center - current.Screen.center;
                float forward = Vector2.Dot(delta, direction);
                if (forward < 1) continue;
                float across = Mathf.Abs(delta.x * direction.y - delta.y * direction.x);
                // Overlapping columns/rows take precedence over diagonally distant controls.
                float overlap = direction.x == 0
                    ? Mathf.Max(0, Mathf.Max(current.Screen.xMin - candidate.Screen.xMax, candidate.Screen.xMin - current.Screen.xMax))
                    : Mathf.Max(0, Mathf.Max(current.Screen.yMin - candidate.Screen.yMax, candidate.Screen.yMin - current.Screen.yMax));
                float candidateScore = forward + across * .8f + overlap * 3;
                if (candidateScore < score) { score = candidateScore; best = candidate; }
            }
            return best;
        }

        public void Suspend()
        { activate = -1; adjust = 0; heldDirection = 0; scrollMotion = mapMotion = Vector2.zero; zoomMotion = 0; }

        public void Update(bool enabled, Action rootBack)
        {
            if (!enabled) { Suspend(); return; }
            var pad = Gamepad.current; var keyboard = Keyboard.current;
            bool textFocused = GUIUtility.keyboardControl != 0;
            bool back = pad?.buttonEast.wasPressedThisFrame == true || (!textFocused && keyboard?.backspaceKey.wasPressedThisFrame == true);
            bool confirm = pad?.buttonSouth.wasPressedThisFrame == true || (!textFocused && keyboard?.enterKey.wasPressedThisFrame == true);
            var selected = Focused;
            float seconds = Mathf.Min(Time.unscaledDeltaTime, .05f);
            Vector2 rightStick = pad?.rightStick.ReadValue() ?? Vector2.zero;
            scrollMotion = rightStick.sqrMagnitude > .05f ? new Vector2(rightStick.x, -rightStick.y) * (650 * seconds) : Vector2.zero;
            mapMotion = Vector2.zero; zoomMotion = 0;
            if (selected?.Kind == ControlKind.Map)
            {
                var stick = pad?.leftStick.ReadValue() ?? Vector2.zero;
                mapMotion = (stick.sqrMagnitude > .08f ? stick : Vector2.zero) + (rightStick.sqrMagnitude > .08f ? rightStick : Vector2.zero);
                mapMotion *= seconds;
                zoomMotion = ((pad?.leftTrigger.ReadValue() ?? 0) - (pad?.rightTrigger.ReadValue() ?? 0)) * seconds;
            }
            Vector2 axis = pad?.dpad.ReadValue() ?? Vector2.zero;
            if (axis.sqrMagnitude < .2f && selected?.Kind != ControlKind.Map) axis = pad?.leftStick.ReadValue() ?? Vector2.zero;
            if (!textFocused && keyboard != null)
            {
                axis.x += (keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0);
                axis.y += (keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.downArrowKey.isPressed ? 1 : 0);
            }
            int direction = Mathf.Max(Mathf.Abs(axis.x), Mathf.Abs(axis.y)) < .55f ? 0
                : Mathf.Abs(axis.x) > Mathf.Abs(axis.y) ? (axis.x > 0 ? 2 : 1) : (axis.y > 0 ? 3 : 4);
            bool move = direction != 0 && (direction != heldDirection || Time.unscaledTime >= repeatAt);
            if (direction != heldDirection) repeatAt = Time.unscaledTime + RepeatDelaySeconds;
            else if (move) repeatAt = Time.unscaledTime + RepeatIntervalSeconds;
            heldDirection = direction;
            if (back)
            {
                ControllerActive = true; activate = -1; adjust = 0;
                bool confirmation = controls.Exists(c => c.Label.StartsWith("CONFIRM", StringComparison.OrdinalIgnoreCase) || c.Label.StartsWith("TRUST THIS", StringComparison.OrdinalIgnoreCase));
                var cancel = confirmation ? controls.Find(c => c.Label.StartsWith("CANCEL", StringComparison.OrdinalIgnoreCase)) : null;
                var target = cancel ?? controls.Find(c => c.Label.StartsWith("BACK", StringComparison.OrdinalIgnoreCase))
                    ?? controls.Find(c => c.Label.StartsWith("CANCEL / RETURN", StringComparison.OrdinalIgnoreCase))
                    ?? controls.Find(c => c.Label == "CANCEL TYPING");
                Feedback(MenuPulse.Back);
                if (target != null) activate = target.Id; else if (editing) editing = false; else rootBack?.Invoke();
                return;
            }
            if (confirm)
            {
                ControllerActive = true;
                if (selected == null) Feedback(MenuPulse.Unavailable);
                else { activate = selected.Id; Feedback(MenuPulse.Confirm); }
                return;
            }
            if (!move) return;
            ControllerActive = true;
            if ((direction == 1 || direction == 2) && (selected?.Kind == ControlKind.Choice || selected?.Kind == ControlKind.Slider))
            { adjust = direction == 2 ? 1 : -1; Feedback(MenuPulse.Move); return; }
            var vector = direction == 1 ? Vector2.left : direction == 2 ? Vector2.right : direction == 3 ? Vector2.down : Vector2.up;
            var next = Neighbour(controls, selected, vector);
            if (next == null) Feedback(MenuPulse.Unavailable);
            else { focused = next.Id; GUIUtility.keyboardControl = 0; Feedback(MenuPulse.Move); }
        }

        internal Vector2 BeginScroll(Rect rect, Vector2 position, Rect content)
        {
            int id = Register(rect, "Scroll list", ControlKind.Scroll);
            if (Event.current.type == EventType.Layout && (!scrolledFrames.TryGetValue(id, out int frame) || frame != Time.frameCount))
            {
                scrolledFrames[id] = Time.frameCount;
                var selected = Focused;
                if (selected != null && selected.ScrollId == id)
                {
                    var top = GUIUtility.GUIToScreenPoint(rect.position);
                    var end = GUIUtility.GUIToScreenPoint(rect.position + rect.size);
                    float scale = Mathf.Max(.01f, (end.y - top.y) / rect.height);
                    if (selected.Screen.yMin < top.y) position.y -= (top.y - selected.Screen.yMin) / scale;
                    else if (selected.Screen.yMax > end.y) position.y += (selected.Screen.yMax - end.y) / scale;
                    if (selected.Screen.xMin < top.x) position.x -= (top.x - selected.Screen.xMin) / scale;
                    else if (selected.Screen.xMax > end.x) position.x += (selected.Screen.xMax - end.x) / scale;
                }
                if (selected != null && (selected.Id == id || selected.ScrollId == id)) position += scrollMotion;
                position.x = Mathf.Clamp(position.x, 0, Mathf.Max(0, content.width - rect.width));
                position.y = Mathf.Clamp(position.y, 0, Mathf.Max(0, content.height - rect.height));
            }
            scrolls.Push(id); return position;
        }
        internal void EndScroll() { if (scrolls.Count > 0) scrolls.Pop(); }
        internal void MapMotion(int id, out Vector2 movement, out float zoom)
        {
            movement = Vector2.zero; zoom = 0;
            if (focused != id || Event.current.type != EventType.Layout) return;
            movement = mapMotion; zoom = zoomMotion; mapMotion = Vector2.zero; zoomMotion = 0;
        }
        internal string TextValue(int id, string value, int maximum)
        {
            string key = context + "/" + id;
            if (completedTarget == key) { value = completedText; completedTarget = ""; }
            if (TakeActivation(id))
            { textTarget = key; typed = value; editLimit = maximum; textTitle = "Enter text"; editing = true; upper = false; }
            return value;
        }
        public void DrawKeyboard()
        {
            GUI.Box(new Rect(130, 225, 1180, 620), "CONTROLLER KEYBOARD");
            GUI.Label(new Rect(165, 265, 1100, 30), textTitle + " · A / Cross: key · B / Circle: cancel · select DONE to apply");
            typed = GUI.TextField(new Rect(165, 310, 1100, 48), typed, editLimit);
            string[] rows = { "1234567890", "qwertyuiop", "asdfghjkl-", "zxcvbnm_./", ":@\\,!?()[]" };
            for (int row = 0; row < rows.Length; row++) for (int column = 0; column < rows[row].Length; column++)
            {
                string key = rows[row][column].ToString(); if (upper) key = key.ToUpperInvariant();
                if (ControllerMenuGUI.Button(new Rect(165 + column * 110, 380 + row * 57, 100, 48), key) && typed.Length < editLimit) typed += key;
            }
            if (ControllerMenuGUI.Button(new Rect(165, 685, 170, 44), upper ? "LOWER CASE" : "UPPER CASE")) upper = !upper;
            if (ControllerMenuGUI.Button(new Rect(350, 685, 170, 44), "SPACE") && typed.Length < editLimit) typed += " ";
            if (ControllerMenuGUI.Button(new Rect(535, 685, 170, 44), "DELETE")) typed = typed.Length > 0 ? typed.Substring(0, typed.Length - 1) : "";
            if (ControllerMenuGUI.Button(new Rect(720, 685, 170, 44), "CLEAR")) typed = "";
            if (ControllerMenuGUI.Button(new Rect(165, 765, 330, 44), "CANCEL TYPING")) editing = false;
            if (ControllerMenuGUI.Button(new Rect(920, 765, 345, 44), "DONE"))
            { completedTarget = textTarget; completedText = typed; editing = false; }
        }
    }
}
