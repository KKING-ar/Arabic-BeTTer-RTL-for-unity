// ArabicFixer.cs
//
// Pure C# (no UnityEngine dependency) Arabic shaping + visual RTL reordering engine.
// This is intentionally engine-agnostic so it can be unit tested outside Unity and
// reused by any renderer that lays glyphs out left-to-right (which is what
// TextMeshPro does under the hood, even for right-to-left languages).
//
// WHAT THIS DOES
// 1. Contextual Arabic letter shaping (isolated / initial / medial / final forms),
//    including Lam-Alef ligatures.
// 2. Visual reordering: within each line, Arabic runs are shaped then character-reversed,
//    while Latin words, numbers, and TMP tags/sprites keep their internal character
//    order untouched and are only *repositioned* as whole tokens.
// 3. Per-line processing: multiple lines are each fixed independently and rejoined in
//    their ORIGINAL top-to-bottom order. The algorithm never reverses line order.
// 4. Bracket/parenthesis mirroring for correct visual nesting in RTL context.
// 5. Diacritics (tashkeel) stay glued to their base letter through shaping and reversal.
//
// WHAT THIS DELIBERATELY DOES NOT DO
// - Full Unicode Bidirectional Algorithm (UAX #9) with nested embedding levels,
//   explicit directional overrides (LRE/RLE/PDF), etc. This implementation uses the
//   simplified "detect base direction per line, split into runs, reverse run order"
//   approach that essentially every Unity Arabic-support plugin uses in practice,
//   because Unity/TextMeshPro has no built-in bidi/shaping engine.
// - Ligatures beyond Lam-Alef (e.g. some decorative/typographic ligatures used in
//   calligraphic fonts) are not special-cased.
//
// IMPORTANT FONT REQUIREMENT
// This shapes text into the Unicode "Arabic Presentation Forms-A/B" code points
// (U+FB50-FDFF, U+FE70-U+FEFF). Your TMP Font Asset MUST include glyphs for those
// ranges (in addition to the base Arabic block U+0600-U+06FF for any fallback/raw
// text) or shaped letters will render as missing-glyph boxes. See README.md.

using System;
using System.Collections.Generic;
using System.Text;

namespace ArabicUnityRTL.Core
{
    public static class ArabicFixer
    {
        private enum TokenType { Arabic, Latin, Number, Tag, Whitespace, Neutral }

        private struct ShapeForms
        {
            public int Isolated;
            public int Initial;  // -1 if not applicable
            public int Medial;   // -1 if not applicable
            public int Final;    // -1 if not applicable

            public ShapeForms(int iso, int init, int med, int fin)
            {
                Isolated = iso; Initial = init; Medial = med; Final = fin;
            }
        }

        private const int NONE = -1;

        // base char -> presentation forms
        private static readonly Dictionary<char, ShapeForms> ShapeTable = new Dictionary<char, ShapeForms>
        {
            ['\u0621'] = new ShapeForms(0xFE80, NONE, NONE, NONE),        // hamza
            ['\u0622'] = new ShapeForms(0xFE81, NONE, NONE, 0xFE82),      // alef madda
            ['\u0623'] = new ShapeForms(0xFE83, NONE, NONE, 0xFE84),      // alef hamza above
            ['\u0624'] = new ShapeForms(0xFE85, NONE, NONE, 0xFE86),      // waw hamza
            ['\u0625'] = new ShapeForms(0xFE87, NONE, NONE, 0xFE88),      // alef hamza below
            ['\u0626'] = new ShapeForms(0xFE89, 0xFE8B, 0xFE8C, 0xFE8A),  // yeh hamza
            ['\u0627'] = new ShapeForms(0xFE8D, NONE, NONE, 0xFE8E),      // alef
            ['\u0628'] = new ShapeForms(0xFE8F, 0xFE91, 0xFE92, 0xFE90),  // beh
            ['\u0629'] = new ShapeForms(0xFE93, NONE, NONE, 0xFE94),      // teh marbuta
            ['\u062A'] = new ShapeForms(0xFE95, 0xFE97, 0xFE98, 0xFE96),  // teh
            ['\u062B'] = new ShapeForms(0xFE99, 0xFE9B, 0xFE9C, 0xFE9A),  // theh
            ['\u062C'] = new ShapeForms(0xFE9D, 0xFE9F, 0xFEA0, 0xFE9E),  // jeem
            ['\u062D'] = new ShapeForms(0xFEA1, 0xFEA3, 0xFEA4, 0xFEA2),  // hah
            ['\u062E'] = new ShapeForms(0xFEA5, 0xFEA7, 0xFEA8, 0xFEA6),  // khah
            ['\u062F'] = new ShapeForms(0xFEA9, NONE, NONE, 0xFEAA),      // dal
            ['\u0630'] = new ShapeForms(0xFEAB, NONE, NONE, 0xFEAC),      // thal
            ['\u0631'] = new ShapeForms(0xFEAD, NONE, NONE, 0xFEAE),      // reh
            ['\u0632'] = new ShapeForms(0xFEAF, NONE, NONE, 0xFEB0),      // zain
            ['\u0633'] = new ShapeForms(0xFEB1, 0xFEB3, 0xFEB4, 0xFEB2),  // seen
            ['\u0634'] = new ShapeForms(0xFEB5, 0xFEB7, 0xFEB8, 0xFEB6),  // sheen
            ['\u0635'] = new ShapeForms(0xFEB9, 0xFEBB, 0xFEBC, 0xFEBA),  // sad
            ['\u0636'] = new ShapeForms(0xFEBD, 0xFEBF, 0xFEC0, 0xFEBE),  // dad
            ['\u0637'] = new ShapeForms(0xFEC1, 0xFEC3, 0xFEC4, 0xFEC2),  // tah
            ['\u0638'] = new ShapeForms(0xFEC5, 0xFEC7, 0xFEC8, 0xFEC6),  // zah
            ['\u0639'] = new ShapeForms(0xFEC9, 0xFECB, 0xFECC, 0xFECA),  // ain
            ['\u063A'] = new ShapeForms(0xFECD, 0xFECF, 0xFED0, 0xFECE),  // ghain
            ['\u0641'] = new ShapeForms(0xFED1, 0xFED3, 0xFED4, 0xFED2),  // feh
            ['\u0642'] = new ShapeForms(0xFED5, 0xFED7, 0xFED8, 0xFED6),  // qaf
            ['\u0643'] = new ShapeForms(0xFED9, 0xFEDB, 0xFEDC, 0xFEDA),  // kaf
            ['\u0644'] = new ShapeForms(0xFEDD, 0xFEDF, 0xFEE0, 0xFEDE),  // lam
            ['\u0645'] = new ShapeForms(0xFEE1, 0xFEE3, 0xFEE4, 0xFEE2),  // meem
            ['\u0646'] = new ShapeForms(0xFEE5, 0xFEE7, 0xFEE8, 0xFEE6),  // noon
            ['\u0647'] = new ShapeForms(0xFEE9, 0xFEEB, 0xFEEC, 0xFEEA),  // heh
            ['\u0648'] = new ShapeForms(0xFEED, NONE, NONE, 0xFEEE),      // waw
            ['\u0649'] = new ShapeForms(0xFEEF, NONE, NONE, 0xFEF0),      // alef maksura
            ['\u064A'] = new ShapeForms(0xFEF1, 0xFEF3, 0xFEF4, 0xFEF2),  // yeh
        };

        // Letters that never connect to the letter that follows them.
        private static readonly HashSet<char> NonConnectingAfter = new HashSet<char>
        {
            '\u0621', '\u0622', '\u0623', '\u0624', '\u0625', '\u0627',
            '\u0629', '\u062F', '\u0630', '\u0631', '\u0632', '\u0648', '\u0649',
        };

        // (lam, following-alef-variant) -> (isolated ligature, final ligature)
        private static readonly Dictionary<(char, char), (int iso, int fin)> LamAlef =
            new Dictionary<(char, char), (int, int)>
        {
            [('\u0644', '\u0622')] = (0xFEF5, 0xFEF6),
            [('\u0644', '\u0623')] = (0xFEF7, 0xFEF8),
            [('\u0644', '\u0625')] = (0xFEF9, 0xFEFA),
            [('\u0644', '\u0627')] = (0xFEFB, 0xFEFC),
        };

        private static readonly HashSet<char> Tashkeel = new HashSet<char>
        {
            '\u064B', '\u064C', '\u064D', '\u064E', '\u064F', '\u0650',
            '\u0651', '\u0652', '\u0653', '\u0654', '\u0655', '\u0656', '\u0670',
        };

        private static readonly Dictionary<char, char> MirrorPairs = new Dictionary<char, char>
        {
            ['('] = ')', [')'] = '(',
            ['['] = ']', [']'] = '[',
            ['{'] = '}', ['}'] = '{',
            ['\u00AB'] = '\u00BB', ['\u00BB'] = '\u00AB',
        };

        private static bool IsArabicLetter(char c) => ShapeTable.ContainsKey(c);
        private static bool IsTashkeel(char c) => Tashkeel.Contains(c);
        private static bool IsLatin(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static TokenType Classify(char c)
        {
            if (IsArabicLetter(c) || IsTashkeel(c)) return TokenType.Arabic;
            if (IsDigit(c)) return TokenType.Number;
            if (IsLatin(c)) return TokenType.Latin;
            if (char.IsWhiteSpace(c)) return TokenType.Whitespace;
            return TokenType.Neutral;
        }

        private struct Token
        {
            public TokenType Type;
            public string Text;
            public Token(TokenType t, string s) { Type = t; Text = s; }
        }

        /// <summary>
        /// Fixes an entire (possibly multi-line) string for RTL Arabic display in
        /// left-to-right renderers such as Unity's TextMeshPro. Handles mixed
        /// Arabic/Latin/number content, TMP rich-text and &lt;sprite&gt; tags, and
        /// preserves the original top-to-bottom order of lines.
        /// </summary>
        public static string Fix(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Split on '\n' only; keep it simple and predictable. Each line is fixed
            // independently and lines are rejoined in their ORIGINAL order so a
            // multi-line RTL paragraph still reads top-to-bottom.
            string[] lines = input.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = FixLine(lines[i]);
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Some RTL text editors/tools produce tags in REVERSED order -
        /// "&lt;/tag&gt;content&lt;tag=attrs&gt;" instead of the standard
        /// "&lt;tag=attrs&gt;content&lt;/tag&gt;" - because the closing marker ends
        /// up typed/stored before the opening one when authoring inside an
        /// RTL-aware editor. This detects that pattern and rewrites it into
        /// standard order so the rest of the pipeline (and TMP itself, which
        /// only understands standard order) can parse it correctly.
        ///
        /// Call this BEFORE Fix() (or before building wrapped text) whenever
        /// the source text is known to use this reversed convention. Nested
        /// reversed pairs of the same tag name are depth-tracked. A closing
        /// tag with no matching opener anywhere later in `input` is left
        /// exactly as-is (not actually a reversed pair - most likely a
        /// genuinely stray tag). An ordinary tag already in standard order
        /// passes through untouched, so this is safe to call even on text
        /// that mixes both conventions... but is really designed for text
        /// that's consistently written backwards throughout.
        /// </summary>
        public static string NormalizeReversedTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            StringBuilder result = new StringBuilder(input.Length);
            int i = 0;
            int n = input.Length;
            while (i < n)
            {
                if (input[i] != '<') { result.Append(input[i]); i++; continue; }
                int close = input.IndexOf('>', i);
                if (close == -1) { result.Append(input[i]); i++; continue; }

                string tagText = input.Substring(i, close - i + 1);
                string inner = tagText.Substring(1, tagText.Length - 2);

                if (inner.Length > 0 && inner[0] == '/')
                {
                    string tagName = inner.Substring(1);
                    int depth = 1;
                    int searchPos = close + 1;
                    int openStart = -1, openEnd = -1;
                    while (searchPos < n)
                    {
                        int nextLt = input.IndexOf('<', searchPos);
                        if (nextLt == -1) break;
                        int nextGt = input.IndexOf('>', nextLt);
                        if (nextGt == -1) break;

                        string innerTag = input.Substring(nextLt + 1, nextGt - nextLt - 1);
                        if (innerTag.Length > 0 && innerTag[0] == '/')
                        {
                            if (string.Equals(innerTag.Substring(1), tagName, StringComparison.OrdinalIgnoreCase))
                                depth++; // nested reversed pair: another closer before our real opener
                        }
                        else
                        {
                            if (string.Equals(ExtractTagName(innerTag), tagName, StringComparison.OrdinalIgnoreCase))
                            {
                                depth--;
                                if (depth == 0) { openStart = nextLt; openEnd = nextGt; break; }
                            }
                        }
                        searchPos = nextGt + 1;
                    }

                    if (openStart != -1)
                    {
                        string openTagText = input.Substring(openStart, openEnd - openStart + 1);
                        string content = input.Substring(close + 1, openStart - (close + 1));
                        string normalizedContent = NormalizeReversedTags(content);
                        result.Append(openTagText).Append(normalizedContent).Append(tagText);
                        i = openEnd + 1;
                        continue;
                    }
                    // No forward-matching opener - leave the closing tag as-is.
                    result.Append(tagText);
                    i = close + 1;
                    continue;
                }

                // Ordinary (opening/void) tag: pass through unchanged - a
                // normal <tag>...</tag> pair is already in the order the
                // rest of the pipeline expects.
                result.Append(tagText);
                i = close + 1;
            }
            return result.ToString();
        }

        /// <summary>
        /// Shapes Arabic letters into the contextual presentation-form glyphs
        /// that will actually be rendered (initial/medial/final/isolated
        /// forms), WITHOUT performing any visual RTL reordering. Everything
        /// else (Latin, numbers, tags, whitespace) is left untouched and
        /// token order is preserved exactly as written.
        ///
        /// This exists purely for WIDTH MEASUREMENT during word-wrapping.
        /// Presentation-form glyphs can have noticeably different advance
        /// widths than the base (unshaped) Arabic letters, so measuring the
        /// raw input string produces inaccurate wrap decisions - some lines
        /// end up overflowing while others break after only a couple of
        /// words. Measuring this shaped-but-unreordered string instead
        /// matches what TMP will actually draw, glyph-for-glyph.
        /// </summary>
        public static string ShapeForMeasurement(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            List<Token> tokens = Tokenize(input);
            StringBuilder sb = new StringBuilder(input.Length);
            foreach (var t in tokens)
            {
                sb.Append(t.Type == TokenType.Arabic ? ShapeArabicRun(t.Text) : t.Text);
            }
            return sb.ToString();
        }

        private static string FixLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            if (!ContainsArabicChar(line)) return line;
            return FixLineBody(line);
        }

        private static bool ContainsArabicChar(string s)
        {
            foreach (char ch in s) { if (IsArabicLetter(ch) || IsTashkeel(ch)) return true; }
            return false;
        }

        /// <summary>
        /// The core algorithm, made recursive: a tag whose matching close is
        /// found ANYWHERE later in `content` (regardless of how many words
        /// or spaces sit between them) has everything between the two tags
        /// recursively re-fixed as its own independent span, then
        /// reassembled as exactly openTag + fixedInside + closeTag - in that
        /// deterministic order, every time. This means:
        ///  - a tag that's already written correctly is NEVER touched or
        ///    flipped, no matter how many words it wraps or how it's nested;
        ///  - only ONE open + ONE close ever appears per originally-authored
        ///    tag (no per-word duplication) as long as the whole span lives
        ///    on one physical line;
        ///  - a tag with no matching close anywhere in `content` (e.g. a
        ///    standalone &lt;sprite&gt; tag) falls through to the ordinary
        ///    whitespace-bounded chain handling below, unchanged.
        /// </summary>
        private static string FixLineBody(string content)
        {
            if (!ContainsArabicChar(content)) return content; // pure LTR span: leave untouched

            List<Token> tokens = Tokenize(content);

            List<Token> processed = new List<Token>(tokens.Count);
            int ti = 0;
            while (ti < tokens.Count)
            {
                Token t = tokens[ti];

                if (t.Type == TokenType.Tag)
                {
                    string inner = t.Text.Substring(1, t.Text.Length - 2);
                    if (inner.Length > 0 && inner[0] != '/')
                    {
                        string tagName = ExtractTagName(inner);
                        int closeIdx = FindMatchingCloseToken(tokens, ti, tagName);
                        if (closeIdx != -1)
                        {
                            StringBuilder innerRaw = new StringBuilder();
                            bool hasArabicInside = false;
                            for (int k = ti + 1; k < closeIdx; k++)
                            {
                                innerRaw.Append(tokens[k].Text);
                                if (tokens[k].Type == TokenType.Arabic) hasArabicInside = true;
                            }

                            string fixedInside = FixLineBody(innerRaw.ToString());
                            string combined = t.Text + fixedInside + tokens[closeIdx].Text;
                            processed.Add(new Token(hasArabicInside ? TokenType.Arabic : TokenType.Tag, combined));
                            ti = closeIdx + 1;
                            continue;
                        }
                        // else: no matching close anywhere in this content -
                        // void/standalone tag (e.g. <sprite>). Falls through
                        // to the ordinary handling below, unchanged.
                    }
                }

                if (t.Type == TokenType.Arabic || t.Type == TokenType.Tag)
                {
                    int start = ti;
                    ti++;
                    // Continuation (not the start) absorbs any directly-
                    // adjacent non-whitespace token - Neutral punctuation
                    // ('،' glued onto a word), Number, Latin, more Arabic,
                    // more tags. Only whitespace ends a chain. A NEUTRAL
                    // still can't START a chain on its own, so a standalone
                    // bracket like "(Ahmed)" with nothing Arabic/tag-like
                    // before it still falls through to the ordinary
                    // mirror-pair path untouched.
                    while (ti < tokens.Count && tokens[ti].Type != TokenType.Whitespace) ti++;
                    List<Token> chain = tokens.GetRange(start, ti - start);

                    bool chainHasArabic = false;
                    foreach (var ct in chain) if (ct.Type == TokenType.Arabic) { chainHasArabic = true; break; }

                    string mergedText = ShapeAndReverseMixedRun(chain);
                    processed.Add(new Token(chainHasArabic ? TokenType.Arabic : TokenType.Tag, mergedText));
                    continue;
                }

                if (t.Type == TokenType.Neutral && t.Text.Length == 1 && MirrorPairs.TryGetValue(t.Text[0], out char mirrored))
                {
                    processed.Add(new Token(t.Type, mirrored.ToString()));
                }
                else
                {
                    // Latin, Number, Whitespace, other Neutral: content untouched
                    processed.Add(t);
                }
                ti++;
            }

            int n = processed.Count;

            // strong[i]: true=RTL, false=LTR, null=this token has no direction
            // of its own (whitespace / tag / other neutral).
            bool?[] strong = new bool?[n];
            for (int i = 0; i < n; i++)
            {
                if (processed[i].Type == TokenType.Arabic) strong[i] = true;
                else if (processed[i].Type == TokenType.Latin || processed[i].Type == TokenType.Number) strong[i] = false;
            }

            bool[] prevStrong = new bool[n];
            bool cur = true; // default RTL base direction for leading neutrals
            for (int i = 0; i < n; i++)
            {
                if (strong[i].HasValue) cur = strong[i].Value;
                prevStrong[i] = cur;
            }

            bool[] nextStrong = new bool[n];
            bool? curN = null;
            for (int i = n - 1; i >= 0; i--)
            {
                if (strong[i].HasValue) curN = strong[i];
                nextStrong[i] = curN ?? prevStrong[i];
            }

            // isRTL[i]: the resolved direction used to place token i in a run.
            // isolate[i]: true only for a WHITESPACE gap that sits exactly at
            // an RTL/LTR boundary. That gap must stay pinned between the two
            // runs as its own single-space run, instead of being dragged along
            // with whichever run happens to precede it - otherwise reversing
            // run order leaves two words jammed together with no separator
            // (e.g. "مرحباHello") while the space resurfaces somewhere else
            // entirely. Whitespace between two SAME-direction words (e.g. the
            // gap in "line one") is NOT isolated, so those words stay merged
            // into one run and keep their relative order instead of being
            // treated as independently-reorderable runs.
            bool[] isRTL = new bool[n];
            bool[] isolate = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (strong[i].HasValue)
                {
                    isRTL[i] = strong[i].Value;
                    isolate[i] = false;
                }
                else if (processed[i].Type == TokenType.Whitespace)
                {
                    if (prevStrong[i] == nextStrong[i])
                    {
                        isRTL[i] = prevStrong[i];
                        isolate[i] = false;
                    }
                    else
                    {
                        isRTL[i] = prevStrong[i];
                        isolate[i] = true;
                    }
                }
                else
                {
                    // Tag / other Neutral: glue to preceding content.
                    isRTL[i] = prevStrong[i];
                    isolate[i] = false;
                }
            }

            List<List<int>> runs = new List<List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (isolate[i])
                {
                    runs.Add(new List<int> { i });
                    continue;
                }
                List<int> prevRun = runs.Count > 0 ? runs[runs.Count - 1] : null;
                bool prevIsolated = prevRun != null && isolate[prevRun[0]];
                if (prevRun != null && !prevIsolated && isRTL[i] == isRTL[prevRun[0]])
                    prevRun.Add(i);
                else
                    runs.Add(new List<int> { i });
            }

            // The runs themselves flow right-to-left (the line's base direction
            // is RTL since it contains Arabic), so the run ORDER is reversed.
            // Inside an RTL run the words also swap places (word1 word2 ->
            // word2 word1); the letters inside each Arabic word were already
            // reversed above. Inside an LTR run, token order is left exactly as
            // written so English/number phrases keep their reading order.
            runs.Reverse();

            StringBuilder sb = new StringBuilder(content.Length);
            foreach (var run in runs)
            {
                bool rtl = isRTL[run[0]];
                if (rtl)
                {
                    for (int k = run.Count - 1; k >= 0; k--) sb.Append(processed[run[k]].Text);
                }
                else
                {
                    for (int k = 0; k < run.Count; k++) sb.Append(processed[run[k]].Text);
                }
            }
            return sb.ToString();
        }

        private static List<Token> Tokenize(string line)
        {
            List<Token> tokens = new List<Token>();
            int i = 0;
            int n = line.Length;
            while (i < n)
            {
                char c = line[i];

                // TMP rich-text tags and <sprite .../> are captured whole and
                // never shaped/reordered internally.
                if (c == '<')
                {
                    int close = line.IndexOf('>', i);
                    if (close == -1)
                    {
                        tokens.Add(new Token(TokenType.Neutral, c.ToString()));
                        i++;
                        continue;
                    }
                    tokens.Add(new Token(TokenType.Tag, line.Substring(i, close - i + 1)));
                    i = close + 1;
                    continue;
                }

                TokenType cls = Classify(c);
                int j = i + 1;
                if (cls == TokenType.Arabic || cls == TokenType.Latin ||
                    cls == TokenType.Number || cls == TokenType.Whitespace)
                {
                    while (j < n && line[j] != '<' && Classify(line[j]) == cls) j++;
                }
                tokens.Add(new Token(cls, line.Substring(i, j - i)));
                i = j;
            }
            return tokens;
        }

        /// <summary>
        /// Shapes one contiguous run of Arabic letters (with any interleaved tashkeel)
        /// into presentation forms, in LOGICAL (original) order. Reversal is a
        /// separate step so diacritics stay glued to their base letter.
        /// </summary>
        /// <summary>
        /// Searches FORWARD through `tokens` (across whitespace - NOT
        /// stopping at it, unlike ordinary chain-building) for the tag that
        /// closes the opening tag at `openIdx`, honoring nesting depth for
        /// repeated same-name tags. Returns the index of the matching close
        /// token, or -1 if none exists anywhere later (a void/standalone tag
        /// like &lt;sprite&gt;).
        /// </summary>
        private static int FindMatchingCloseToken(List<Token> tokens, int openIdx, string tagName)
        {
            int depth = 1;
            for (int j = openIdx + 1; j < tokens.Count; j++)
            {
                if (tokens[j].Type != TokenType.Tag) continue;
                string inner = tokens[j].Text.Substring(1, tokens[j].Text.Length - 2);
                if (inner.Length > 0 && inner[0] == '/')
                {
                    if (string.Equals(inner.Substring(1), tagName, StringComparison.OrdinalIgnoreCase))
                    {
                        depth--;
                        if (depth == 0) return j;
                    }
                }
                else
                {
                    if (string.Equals(ExtractTagName(inner), tagName, StringComparison.OrdinalIgnoreCase))
                        depth++;
                }
            }
            return -1;
        }

        /// <summary>
        /// Shapes AND visually reverses one contiguous stretch of Arabic +
        /// TMP-tag tokens as a single clustering unit. Tags (e.g. &lt;color&gt;,
        /// &lt;sprite&gt;) inside the run are treated as invisible/atomic:
        /// they never break which letter a diacritic belongs to, they're
        /// never split character-by-character during reversal (so their own
        /// markup text can't get scrambled), and Arabic letter-joining
        /// context (initial/medial/final selection) correctly skips over
        /// them as if they weren't there - matching how TMP renders them
        /// (invisible, non-advancing markup).
        ///
        /// Also fixes a real bug that used to live in the single-token
        /// shaper: a Lam-Alef ligature ("لا") used to silently DROP any
        /// diacritic sitting on the alef, since the ligature branch only
        /// ever re-emitted the lam's own marks before jumping straight past
        /// the alef's cluster. That's fixed here by explicitly carrying the
        /// alef's marks (and re-emitting any tag that happened to sit
        /// between the lam and alef) into the ligature's output.
        /// </summary>
        private static string ShapeAndReverseMixedRun(List<Token> runTokens)
        {
            // Defensive guard: only ever shape+reverse a run that actually
            // contains Arabic. Without this, a chain that turns out to be
            // pure Number/Latin content (e.g. a bare number wrapped by its
            // own tag, "<align>45</align>", processed via the recursive
            // tag-pair handling in FixLineBody, leaving a middle chain of
            // JUST the number token) would still get its
            // clusters blindly reversed character-by-character - turning
            // "45" into "54". Reversal must never apply to content with no
            // Arabic in it at all, regardless of how it got here.
            bool hasArabicToken = false;
            foreach (var rt in runTokens) if (rt.Type == TokenType.Arabic) { hasArabicToken = true; break; }
            if (!hasArabicToken)
            {
                StringBuilder passthrough = new StringBuilder();
                foreach (var rt in runTokens) passthrough.Append(rt.Text);
                return passthrough.ToString();
            }

            // Step 1: flatten into low-level clusters. A "letter" cluster is
            // (base char, its own trailing tashkeel). An "opaque" cluster is
            // either a whole Tag token's raw text (kept as ONE atomic unit,
            // never exploded per-character) or a single stray non-letter
            // character (e.g. a tashkeel mark with no base letter before it).
            List<(bool isLetter, char baseChar, string marks, string opaque)> clusters =
                new List<(bool, char, string, string)>();

            foreach (var tok in runTokens)
            {
                if (tok.Type == TokenType.Tag)
                {
                    clusters.Add((false, '\0', null, tok.Text));
                    continue;
                }

                string text = tok.Text; // Arabic token
                int i = 0;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (IsArabicLetter(c))
                    {
                        int j = i + 1;
                        StringBuilder marks = new StringBuilder();
                        while (j < text.Length && IsTashkeel(text[j])) { marks.Append(text[j]); j++; }
                        clusters.Add((true, c, marks.ToString(), null));
                        i = j;
                    }
                    else
                    {
                        // stray tashkeel/char with no base letter before it
                        clusters.Add((false, '\0', null, text[i].ToString()));
                        i++;
                    }
                }
            }

            // Letter-only sequence for neighbor lookups - skips opaque/tag
            // clusters entirely, so Arabic joining context correctly spans
            // across a tag as if it weren't there.
            List<char> letters = new List<char>();
            foreach (var cl in clusters) if (cl.isLetter) letters.Add(cl.baseChar);

            // Step 2: shape. Produces a flat list of atomic output units -
            // each either a shaped glyph + its own marks, or an opaque
            // passthrough string - still in LOGICAL order.
            List<(string text, string marks)> shapedUnits = new List<(string, string)>(clusters.Count);
            int li = 0;
            for (int idx = 0; idx < clusters.Count; idx++)
            {
                var cl = clusters[idx];
                if (!cl.isLetter)
                {
                    shapedUnits.Add((cl.opaque, string.Empty));
                    continue;
                }

                char baseChar = cl.baseChar;
                char? prevLetter = li > 0 ? letters[li - 1] : (char?)null;
                char? nextLetter = li + 1 < letters.Count ? letters[li + 1] : (char?)null;

                if (baseChar == '\u0644' && nextLetter.HasValue &&
                    LamAlef.TryGetValue((baseChar, nextLetter.Value), out var lig))
                {
                    bool connectsPrev = prevLetter.HasValue && !NonConnectingAfter.Contains(prevLetter.Value);
                    int code = connectsPrev ? lig.fin : lig.iso;

                    // The alef this ligature consumes is the NEXT LETTER
                    // cluster - skip over any opaque (tag) clusters sitting
                    // between the lam and the alef to find it, so a tag
                    // dropped mid-ligature can't break the ligature.
                    int alefIdx = idx + 1;
                    while (alefIdx < clusters.Count && !clusters[alefIdx].isLetter) alefIdx++;

                    string alefMarks = alefIdx < clusters.Count ? clusters[alefIdx].marks : string.Empty;
                    shapedUnits.Add((char.ConvertFromUtf32(code), cl.marks + alefMarks));

                    // Re-emit any opaque (tag) clusters that sat between lam
                    // and alef so they aren't silently dropped.
                    for (int k = idx + 1; k < alefIdx; k++)
                        shapedUnits.Add((clusters[k].opaque, string.Empty));

                    idx = alefIdx; // consumed through the alef cluster
                    li += 2;
                    continue;
                }

                bool cPrev = prevLetter.HasValue && !NonConnectingAfter.Contains(prevLetter.Value);
                bool cNext = nextLetter.HasValue && !NonConnectingAfter.Contains(baseChar) && ShapeTable.ContainsKey(nextLetter.Value);

                ShapeForms forms = ShapeTable[baseChar];
                int outCode;
                if (cPrev && cNext && forms.Medial != NONE) outCode = forms.Medial;
                else if (cPrev && forms.Final != NONE) outCode = forms.Final;
                else if (cNext && forms.Initial != NONE) outCode = forms.Initial;
                else outCode = forms.Isolated;

                shapedUnits.Add((char.ConvertFromUtf32(outCode), cl.marks));
                li++;
            }

            // Step 3: reverse cluster ORDER for RTL visual display. Each
            // shaped unit (glyph + its own marks, or a whole opaque tag)
            // moves as one atomic piece, so tashkeel stays glued to its
            // letter and tag markup is never scrambled internally.
            shapedUnits.Reverse();

            // A mark is READ immediately AFTER its base glyph (postfix
            // modifier). The same "later-read content ends up further left"
            // rule that makes whole-word reversal correct also applies at
            // the single-letter level: since a mark reads AFTER its base
            // letter, it must be WRITTEN BEFORE (to the left of) that
            // letter's glyph in the output stream - otherwise, since TMP
            // draws left-to-right with no bidi/GPOS repositioning, the mark
            // visually lands one letter too early when read right-to-left
            // (e.g. a tanween on a word's last letter appears to sit on the
            // second-to-last letter instead).
            StringBuilder sb = new StringBuilder();
            foreach (var (text, marks) in shapedUnits)
            {
                sb.Append(marks);
                sb.Append(text);
            }
            return sb.ToString();
        }

        private static string ExtractTagName(string tagInner)
        {
            int cut = tagInner.IndexOfAny(new[] { '=', ' ' });
            return cut == -1 ? tagInner : tagInner.Substring(0, cut);
        }

        /// <summary>
        /// One top-level matching open/close tag pair found by
        /// <see cref="FindTopLevelTagPairs"/>, given as character offsets
        /// into the original searched string.
        /// </summary>
        public struct TagPair
        {
            public int OpenStart, OpenEnd, CloseStart, CloseEnd;
            public string TagName;
        }

        /// <summary>
        /// Scans `text` left to right and returns every TOP-LEVEL matching
        /// open/close tag pair as character offsets into `text`. Nested tags
        /// of the SAME name are depth-tracked so the correct close is
        /// matched. A tag with no matching close anywhere in `text` (a
        /// void/standalone tag like &lt;sprite=...&gt;) is simply skipped - it
        /// never appears in the result.
        ///
        /// Public so callers that do their OWN word-wrapping (like
        /// RTLTextMeshPro's "RTL Wrap") can find which physical line a tag's
        /// open and close ended up on AFTER wrapping, and only then close it
        /// off / reopen it on exactly the lines it actually spans - instead
        /// of speculatively duplicating every tag onto every word up front.
        /// </summary>
        public static List<TagPair> FindTopLevelTagPairs(string text)
        {
            List<TagPair> pairs = new List<TagPair>();
            int i = 0;
            int n = text.Length;
            while (i < n)
            {
                if (text[i] != '<') { i++; continue; }
                int close = text.IndexOf('>', i);
                if (close == -1) { i++; continue; }

                string inner = text.Substring(i + 1, close - i - 1);
                if (inner.Length == 0 || inner[0] == '/') { i = close + 1; continue; }

                string tagName = ExtractTagName(inner);
                int depth = 1;
                int searchPos = close + 1;
                int matchStart = -1, matchEnd = -1;
                while (searchPos < n)
                {
                    int nextLt = text.IndexOf('<', searchPos);
                    if (nextLt == -1) break;
                    int nextGt = text.IndexOf('>', nextLt);
                    if (nextGt == -1) break;

                    string innerTag = text.Substring(nextLt + 1, nextGt - nextLt - 1);
                    if (innerTag.Length > 0 && innerTag[0] == '/')
                    {
                        if (string.Equals(innerTag.Substring(1), tagName, StringComparison.OrdinalIgnoreCase))
                        {
                            depth--;
                            if (depth == 0) { matchStart = nextLt; matchEnd = nextGt; break; }
                        }
                    }
                    else
                    {
                        if (string.Equals(ExtractTagName(innerTag), tagName, StringComparison.OrdinalIgnoreCase))
                            depth++;
                    }
                    searchPos = nextGt + 1;
                }

                if (matchStart == -1) { i = close + 1; continue; } // void/standalone tag

                pairs.Add(new TagPair { OpenStart = i, OpenEnd = close, CloseStart = matchStart, CloseEnd = matchEnd, TagName = tagName });
                i = matchEnd + 1;
            }
            return pairs;
        }

        private static string ShapeArabicRun(string text)
        {
            // Build clusters: each is (baseLetter or '\0', trailingMarks)
            List<(char baseChar, string marks)> clusters = new List<(char, string)>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (IsArabicLetter(c))
                {
                    int j = i + 1;
                    StringBuilder marks = new StringBuilder();
                    while (j < text.Length && IsTashkeel(text[j])) { marks.Append(text[j]); j++; }
                    clusters.Add((c, marks.ToString()));
                    i = j;
                }
                else
                {
                    // stray tashkeel with no base letter before it
                    clusters.Add(('\0', text[i].ToString()));
                    i++;
                }
            }

            // Letter-only sequence for neighbor lookups (skips stray marks).
            List<char> letters = new List<char>();
            foreach (var cl in clusters) if (cl.baseChar != '\0') letters.Add(cl.baseChar);

            StringBuilder result = new StringBuilder();
            int li = 0;
            int idx = 0;
            while (idx < clusters.Count)
            {
                var (baseChar, marks) = clusters[idx];
                if (baseChar == '\0')
                {
                    result.Append(marks);
                    idx++;
                    continue;
                }

                char? prevLetter = li > 0 ? letters[li - 1] : (char?)null;
                char? nextLetter = li + 1 < letters.Count ? letters[li + 1] : (char?)null;

                // Lam-Alef ligature
                if (baseChar == '\u0644' && nextLetter.HasValue &&
                    LamAlef.TryGetValue((baseChar, nextLetter.Value), out var lig))
                {
                    bool connectsPrev = prevLetter.HasValue && !NonConnectingAfter.Contains(prevLetter.Value);
                    int code = connectsPrev ? lig.fin : lig.iso;
                    // Carry the ALEF's own marks too (clusters[idx+1]) - a
                    // Lam-Alef ligature used to silently drop any diacritic
                    // sitting on the alef here, since only the lam's marks
                    // were re-emitted before jumping past the alef cluster.
                    string alefMarks = (idx + 1 < clusters.Count) ? clusters[idx + 1].marks : string.Empty;
                    result.Append(char.ConvertFromUtf32(code));
                    result.Append(marks);
                    result.Append(alefMarks);
                    idx += 2; // consumed lam + alef
                    li += 2;
                    continue;
                }

                bool cPrev = prevLetter.HasValue && !NonConnectingAfter.Contains(prevLetter.Value);
                bool cNext = nextLetter.HasValue && !NonConnectingAfter.Contains(baseChar) && ShapeTable.ContainsKey(nextLetter.Value);

                ShapeForms forms = ShapeTable[baseChar];
                int outCode;
                if (cPrev && cNext && forms.Medial != NONE) outCode = forms.Medial;
                else if (cPrev && forms.Final != NONE) outCode = forms.Final;
                else if (cNext && forms.Initial != NONE) outCode = forms.Initial;
                else outCode = forms.Isolated;

                result.Append(char.ConvertFromUtf32(outCode));
                result.Append(marks);
                idx++;
                li++;
            }

            return result.ToString();
        }

    }
}
