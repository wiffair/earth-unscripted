using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    /// <summary>
    /// QA-only READ-ONLY layout observation after the actual menu Repaint. Never
    /// draws text or calls DrawParagraph. Measures original immutable Core strings
    /// with the actual menu GUIStyles/constants, then requires agreement with the
    /// production Draw return height. Reference pixels follow real DrivingUI scale.
    /// This is geometric viewport coverage, not OCR or automatic readability proof.
    /// </summary>
    public sealed class EngineerHelpScrollAudit
    {
        public sealed class Paragraph
        {
            public string Id;
            public float Start, End;
        }

        public int Frame { get; private set; } = -1;
        public int View { get; private set; }
        public float Scroll { get; private set; }
        public float Height { get; private set; }
        public float Viewport => Constant("ScrollViewportHeightReferencePixels");
        public float MaximumScroll => Mathf.Max(0, Height - Viewport);
        public IReadOnlyList<Paragraph> Rows => rows;

        readonly List<Paragraph> rows = new List<Paragraph>();
        public string Failure { get; private set; }
        public float AdviceStart { get; private set; }

        static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        static object Field(object target, string name) => typeof(RaceSetupMenu).GetField(name, InstanceFlags).GetValue(target);
        static float Constant(string name) => Convert.ToSingle(typeof(RaceSetupMenu).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue());
        float ParagraphAt(float y, string id, string text, GUIStyle style)
        {
            float height = Mathf.Max(Constant("MinimumParagraphHeightReferencePixels"), style.CalcHeight(new GUIContent(text), Constant("ContentWidthReferencePixels")));
            rows.Add(new Paragraph { Id = id, Start = y, End = y + height });
            return y + height + Constant("ParagraphGapReferencePixels");
        }

        public void ObserveRepaint(RaceSetupMenu menu)
        {
            if (menu == null || Event.current.type != EventType.Repaint)
                return;
            int view = (int)Field(menu, "selectedView");
            if (view != 1 && view != 2)
            {
                Frame = -1;
                return;
            }

            try
            {
                var heading = (GUIStyle)Field(menu, "headingStyle");
                var body = (GUIStyle)Field(menu, "bodyStyle");
                if (heading == null || body == null)
                    return;
                rows.Clear();
                float y = 0;
                if (view == 1)
                {
                    if (RaceEngineerRules.Explanations.Count != 12)
                        throw new InvalidOperationException("Explicit12-guide contract changed");
                    foreach (var item in RaceEngineerRules.Explanations)
                    {
                        y = ParagraphAt(y, "guide/" + item.Key + "/title", item.Title, heading);
                        y = ParagraphAt(y, "guide/" + item.Key + "/effect", item.Effect, body);
                        y = ParagraphAt(y, "guide/" + item.Key + "/tradeoff", "Tradeoff: " + item.Tradeoff, body);
                        y += Constant("GuideItemGapReferencePixels");
                    }
                }
                else
                {
                    y = ParagraphAt(0, "questions/introduction", "Tell the engineer what the car does. Suggestions use this game's handling model; try one change at a time.", body);
                    rows.Add(new Paragraph { Id = "questions/symptom", Start = y, End = y + Constant("SymptomQuestionAdvanceReferencePixels") });
                    y += Constant("SymptomQuestionAdvanceReferencePixels");
                    rows.Add(new Paragraph { Id = "questions/phase", Start = y, End = y + Constant("QuestionAdvanceReferencePixels") });
                    y += Constant("QuestionAdvanceReferencePixels");
                    rows.Add(new Paragraph { Id = "questions/surface", Start = y, End = y + Constant("QuestionAdvanceReferencePixels") });
                    y += Constant("QuestionAdvanceReferencePixels");
                    rows.Add(new Paragraph { Id = "questions/highSpeed", Start = y, End = y + Constant("HighSpeedToggleHeightReferencePixels") });
                    y += Constant("HighSpeedToggleAdvanceReferencePixels");
                    AdviceStart = y;
                    var advice = (RaceEngineerAdvice)Field(menu, "engineerAdvice");
                    if (advice == null)
                        y = ParagraphAt(y, "result/prompt", "Choose RECOMMEND below. Nothing will be fitted or saved automatically.", body);
                    else
                    {
                        string status = advice.Status == RaceEngineerStatus.Recommended ? "SUGGESTED STARTING POINT" : advice.Status == RaceEngineerStatus.NoChange ? "NO CHANGE SUGGESTED" : advice.Status == RaceEngineerStatus.Unsupported ? "MORE DETAIL NEEDED" : "CHECK SETUP OR ANSWERS";
                        y = ParagraphAt(y, "result/status", status, heading);
                        y = ParagraphAt(y, "result/summary", advice.Summary, body);
                        foreach (var change in advice.Changes)
                        {
                            string title = (string)typeof(RaceSetupMenu).GetMethod("SettingDisplayName", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { change.Setting });
                            y = ParagraphAt(y, "result/" + change.Setting + "/change", title + ": " + change.Before + " → " + change.After, heading);
                            y = ParagraphAt(y, "result/" + change.Setting + "/benefit", change.Benefit, body);
                            y = ParagraphAt(y, "result/" + change.Setting + "/tradeoff", "Tradeoff: " + change.Tradeoff, body);
                            y += Constant("AdviceChangeGapReferencePixels");
                        }
                    }
                }

                float actualHeight = (float)Field(menu, "contentHeightReferencePixels");
                if (Mathf.Abs(actualHeight - y) > .01f)
                    throw new InvalidOperationException("Read-only paragraph measurement disagrees with actual Draw content height: " + y + " vs " + actualHeight);
                View = view;
                Height = actualHeight;
                Scroll = ((Vector2)Field(menu, "scrollPosition")).y;
                if (Scroll < -.01f || Scroll > MaximumScroll + .5f)
                    throw new InvalidOperationException("Actual GUI scroll outside clamp");
                Frame = Time.frameCount;
            }
            catch (Exception error)
            {
                Failure = error.ToString();
            }
        }

        public JObject Snapshot(float requested)
        {
            if (Failure != null)
                throw new InvalidOperationException(Failure);
            if (Frame != Time.frameCount || Mathf.Abs(Scroll - requested) > .5f)
                throw new InvalidOperationException("Actual current Repaint/clamped requested scroll not observed");
            return new JObject
            {
                ["frame"] = Frame,
                ["view"] = View,
                ["requestedReferenceY"] = requested,
                ["actualReferenceY"] = Scroll,
                ["maximumReferenceY"] = MaximumScroll,
                ["viewportReferenceHeight"] = Viewport,
                ["contentReferenceHeight"] = Height,
                ["visibleParagraphIds"] = new JArray(rows.Where(p => p.End > Scroll && p.Start < Scroll + Viewport).Select(p => p.Id)),
                ["fullyVisibleParagraphIds"] = new JArray(rows.Where(p => p.Start >= Scroll && p.End <= Scroll + Viewport).Select(p => p.Id)),
                ["scope"] = "Actual menu Repaint + original-style geometry; no text drawn by observer/OCR/readability claim"
            };
        }
    }
}
