// RTLTextMeshPro.cs
//
// Drop this on any GameObject that already has a TMP_Text component
// (TextMeshProUGUI for Canvas UI, or TextMeshPro for world-space text).
// Type your normal, logical-order text (Arabic, English, numbers, <sprite>
// tags, TMP rich text tags, all mixed together) into "Source Text" in the
// Inspector, or call SetText(...) at runtime. This component runs it through
// ArabicFixer and pushes the shaped/reordered result into the TMP component.
//
// IMPORTANT: don't edit the TMP_Text's own "Text" field directly once this
// component is attached — it gets overwritten with the processed output.
// Always author/localize through SourceText / SetText().
//
// WORD-WRAP WARNING (read this):
// TextMeshPro's built-in auto word-wrap operates on whatever string it is
// given, breaking lines purely by character position. Since we already
// re-order text for RTL display, if TMP then wraps that reordered string
// automatically, the wrap point can land in the wrong place — worse, it can
// wrap a long mixed Arabic/English paragraph such that whole BLOCKS end up
// on the wrong visual row (e.g. the English tail of the paragraph appears
// as line 1 and the Arabic head as line 2), because TMP has no idea the
// string it's wrapping has already been bidi-reordered.
//
// Three ways to handle this:
//   1. RECOMMENDED / bulletproof: turn ON the "RTL Wrap" toggle below. This
//      disables TMP's own word-wrap and instead measures + breaks the RAW
//      source text into physical lines FIRST (in normal logical reading
//      order), then runs ArabicFixer on each already-correct physical line
//      independently — automatically, every time source text or the box
//      width changes. No manual '\n' authoring needed.
//   2. Manual: turn OFF "Auto Size"/word-wrap overflow on the TMP component
//      and insert your own '\n' line breaks in Source Text where you want
//      lines to break. Simple and predictable, but you own the line breaks.
//   3. Call SetTextWrapped(raw, maxWidth) directly at runtime if you want to
//      drive the wrap width from your own code instead of the RectTransform.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;
using ArabicUnityRTL.Core;

namespace ArabicUnityRTL.Unity
{
    [ExecuteAlways] // lets Update() run in Edit Mode too, so live-wrap resize
                     // detection works without pressing Play (see Update()).
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public class RTLTextMeshPro : MonoBehaviour
    {
        [Tooltip("Type your normal logical-order text here (mixed Arabic/English/numbers/sprites/tags). This is what gets shaped and reordered — don't edit the TMP text field directly.")]
        [TextArea(2, 8)]
        [SerializeField] private string sourceText = string.Empty;

        [Tooltip("Convert Western digits (0-9) to Arabic-Indic digits (٠-٩) in Arabic-context runs. Off by default.")]
        [SerializeField] private bool useArabicIndicDigits = false;

        [Tooltip("Re-apply the fix automatically when values change in the Inspector (Edit Mode preview).")]
        [SerializeField] private bool livePreviewInEditor = true;

        [Tooltip("Disables TextMeshPro's own auto word-wrap and instead breaks the SOURCE text into physical lines BEFORE running the Arabic fixer, then fixes each physical line independently. Keeps top-to-bottom line ORDER correct for wrapped mixed Arabic/English paragraphs instead of letting TMP re-wrap already RTL-reordered text. Turn this on any time your text auto-wraps across more than one line.")]
        [SerializeField] private bool rtlWrap = false;

        [Tooltip("Only used while RTL Wrap is on. OFF (default) = 'live' wrapping: re-measures against this object's actual RectTransform width every time it changes (drag-resize, responsive layout, Auto Size, etc.) and decides line breaks from that. ON = 'fixed' wrapping: always wraps against the Fixed Wrap Width value below, ignoring the RectTransform's actual width entirely - useful when you want wrap points to stay stable regardless of how the box gets resized.")]
        [SerializeField] private bool fixedWrap = false;

        [Tooltip("The width (in the TMP component's local units) used for wrapping when Fixed Wrap is enabled. Ignored while Fixed Wrap is off.")]
        [SerializeField] private float fixedWrapWidth = 400f;

        private TMP_Text _text;
        private float _lastLiveWidth = float.NaN;

        private static readonly char[] WesternDigits = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        private static readonly char[] ArabicIndicDigits = { '\u0660', '\u0661', '\u0662', '\u0663', '\u0664', '\u0665', '\u0666', '\u0667', '\u0668', '\u0669' };

        public string SourceTextValue => sourceText;

        private TMP_Text Text => _text != null ? _text : (_text = GetComponent<TMP_Text>());

        private void OnEnable()
        {
            _lastLiveWidth = float.NaN;
            Apply();
        }

        // OnRectTransformDimensionsChange is NOT reliable for this: Unity
        // fires it inconsistently in Edit Mode - dragging the resize handles
        // in the Scene view usually triggers it, but typing a new value
        // directly into the Width/Height fields in the Inspector often does
        // not. So it's kept here as a fast-path for the cases it DOES catch,
        // and Update() below is the real safety net that guarantees a resize
        // is always picked up regardless of how it happened.
        private void OnRectTransformDimensionsChange()
        {
            if (!rtlWrap || fixedWrap) return;
            if (!isActiveAndEnabled) return;
            Apply();
        }

        // Polls the RectTransform's width once a frame (Edit Mode included,
        // thanks to [ExecuteAlways]) and re-wraps only when it actually
        // changed. This is what makes "live" wrapping reliably follow manual
        // resizes, layout rebuilds, animation, etc. no matter the source of
        // the change. Cheap: a float compare per frame when idle.
        private void Update()
        {
            if (!rtlWrap || fixedWrap) return;
            float w = GetAvailableWidth();
            if (!Mathf.Approximately(w, _lastLiveWidth))
            {
                _lastLiveWidth = w;
                Apply();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!livePreviewInEditor) return;
            // Defer so we don't call SetText mid-inspector-layout.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                Apply();
            };
        }
#endif

        /// <summary>
        /// Sets new logical-order source text at runtime (e.g. from a dialogue
        /// system or localization table) and immediately refreshes the display.
        /// </summary>
        public void SetText(string raw)
        {
            sourceText = raw ?? string.Empty;
            Apply();
        }

        /// <summary>
        /// Like SetText, but performs its own word-wrapping at maxWidth (in the
        /// TMP component's local units) BEFORE shaping/reordering, then disables
        /// TMP's own wrapping so it can't re-break the already-reordered string.
        /// Approximate: matches TMP's line breaks closely but not pixel-perfectly.
        /// This is what the "RTL Wrap" inspector toggle drives automatically,
        /// using the RectTransform's width as maxWidth — call this directly
        /// instead if you want to supply your own width.
        /// </summary>
        public void SetTextWrapped(string raw, float maxWidth)
        {
            sourceText = raw ?? string.Empty;
            if (Text == null) return;

            Text.enableWordWrapping = false;
            Text.text = BuildWrappedProcessedText(sourceText, maxWidth);
        }

        /// <summary>
        /// Breaks `raw` into physical lines that fit `maxWidth` (measuring in
        /// LOGICAL/original order, before any RTL reordering happens), then
        /// runs each already-correct physical line through ArabicFixer and
        /// rejoins them with '\n'. Because line-splitting happens first, line
        /// ORDER always matches the source paragraph's natural reading order
        /// top-to-bottom — only the content *within* each line gets shaped
        /// and visually reordered for RTL.
        /// </summary>
        private string BuildWrappedProcessedText(string raw, float maxWidth)
        {
            string[] paragraphs = (raw ?? string.Empty).Split('\n');
            List<string> outputLines = new List<string>();

            foreach (string paragraph in paragraphs)
            {
                if (paragraph.Length == 0) { outputLines.Add(string.Empty); continue; }

                string[] words = paragraph.Split(' ');
                StringBuilder currentLine = new StringBuilder();

                foreach (string word in words)
                {
                    string candidate = currentLine.Length == 0 ? word : currentLine + " " + word;
                    float width = MeasureWidth(candidate);

                    if (width > maxWidth && currentLine.Length > 0)
                    {
                        outputLines.Add(currentLine.ToString());
                        currentLine.Clear();
                        currentLine.Append(word);
                    }
                    else
                    {
                        if (currentLine.Length > 0) currentLine.Append(' ');
                        currentLine.Append(word);
                    }
                }
                if (currentLine.Length > 0) outputLines.Add(currentLine.ToString());
            }

            string rebuilt = string.Join("\n", outputLines);
            return ArabicFixer.Fix(useArabicIndicDigits ? ToArabicIndicDigits(rebuilt) : rebuilt);
        }

        private float MeasureWidth(string s)
        {
            // Measure the SHAPED glyph form (what TMP will actually draw),
            // not the raw un-shaped Arabic letters. Arabic presentation-form
            // glyphs (initial/medial/final/isolated) commonly have different
            // advance widths than the base letters, so measuring raw text
            // makes wrap decisions that don't match reality - lines look
            // full or nearly empty depending on how much a given word's
            // shaped width diverged from its raw-text estimate. Reordering
            // doesn't affect total width, only shaping does, so this uses
            // ArabicFixer.ShapeForMeasurement (shape only, no reorder).
            string shaped = ArabicFixer.ShapeForMeasurement(s);
            Vector2 size = Text.GetPreferredValues(shaped, 0, 0);
            return size.x;
        }

        /// <summary>
        /// Current available width (in the TMP component's local units) to
        /// wrap against when RTL Wrap is on. In "fixed" mode this is always
        /// fixedWrapWidth, no matter what size the box actually is. In "live"
        /// mode it comes from this object's own RectTransform, since both
        /// TextMeshProUGUI and world-space TextMeshPro use a RectTransform.
        /// </summary>
        private float GetAvailableWidth()
        {
            if (fixedWrap) return fixedWrapWidth;
            RectTransform rt = transform as RectTransform;
            return rt != null ? rt.rect.width : 0f;
        }

        private void Apply()
        {
            if (Text == null) return;

            if (rtlWrap)
            {
                Text.enableWordWrapping = false;
                float maxWidth = GetAvailableWidth();
                if (maxWidth > 0f)
                {
                    if (!fixedWrap) _lastLiveWidth = maxWidth;
                    Text.text = BuildWrappedProcessedText(sourceText, maxWidth);
                    return;
                }
                // RectTransform width isn't known yet this frame (e.g. layout
                // hasn't run) — fall through so text isn't left blank; it will
                // self-correct on the next OnRectTransformDimensionsChange.
            }

            string processed = ArabicFixer.Fix(useArabicIndicDigits ? ToArabicIndicDigits(sourceText) : sourceText);
            Text.text = processed;
        }

        private static string ToArabicIndicDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            char[] chars = s.ToCharArray();
            bool insideTag = false;
            for (int i = 0; i < chars.Length; i++)
            {
                // Skip digit conversion entirely while inside a <...> tag -
                // sprite indices, color hex values, size attributes, etc.
                // are TMP/attribute syntax and MUST stay literal Western
                // digits or the tag stops parsing correctly (e.g.
                // <sprite=2> silently breaking into plain visible text).
                if (chars[i] == '<') { insideTag = true; continue; }
                if (chars[i] == '>') { insideTag = false; continue; }
                if (insideTag) continue;

                for (int d = 0; d < WesternDigits.Length; d++)
                {
                    if (chars[i] == WesternDigits[d]) { chars[i] = ArabicIndicDigits[d]; break; }
                }
            }
            return new string(chars);
        }
    }
}
