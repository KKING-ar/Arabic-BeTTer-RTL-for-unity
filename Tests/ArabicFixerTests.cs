// ArabicFixerTests.cs
//
// Unity Test Framework (NUnit) edit-mode tests. Put this file in a folder
// named "Tests" with an asmdef that references "UnityEngine.TestRunner",
// "UnityEditor.TestRunner", and your runtime assembly (or just drop it in
// Assets/Tests and let Unity's default Tests assembly definition pick it up).
//
// Expected values below were cross-checked against an equivalent Python
// implementation of the same algorithm (see prototype.py in the delivered
// package) so you have independent confirmation, not just "trust me".
//
// Run via Window > General > Test Runner > EditMode > Run All.

using NUnit.Framework;
using ArabicUnityRTL.Core;

public class ArabicFixerTests
{
    [Test]
    public void PureLatinLine_IsUntouched()
    {
        string input = "Hello World 123";
        Assert.AreEqual(input, ArabicFixer.Fix(input));
    }

    [Test]
    public void SimpleArabicWord_IsShapedAndReversed()
    {
        // "مرحبا" (marhaba / hello)
        string input = "\u0645\u0631\u062D\u0628\u0627";
        string expected = "\uFE8E\uFE92\uFEA3\uFEAE\uFEE3";
        Assert.AreEqual(expected, ArabicFixer.Fix(input));
    }

    [Test]
    public void MixedArabicEnglish_KeepsEnglishWordIntactAndUnreversed()
    {
        string input = "Hello \u0645\u0631\u062D\u0628\u0627 World";
        string result = ArabicFixer.Fix(input);
        StringAssert.Contains("Hello", result);
        StringAssert.Contains("World", result);
        // English words must appear character-for-character forward, never reversed
        StringAssert.DoesNotContain("olleH", result);
        StringAssert.DoesNotContain("dlroW", result);
    }

    [Test]
    public void NumbersAreNeverDigitReversed()
    {
        // "السنة 2024 كانت جيدة" (the year 2024 was good)
        string input = "\u0627\u0644\u0633\u0646\u0629 2024 \u0643\u0627\u0646\u062A \u062C\u064A\u062F\u0629";
        string result = ArabicFixer.Fix(input);
        StringAssert.Contains("2024", result);
        StringAssert.DoesNotContain("4202", result);
    }

    [Test]
    public void SpriteTagIsPreservedVerbatimAndNotShaped()
    {
        string input = "\u0645\u0631\u062D\u0628\u0627<sprite name=\"coin\">!";
        string result = ArabicFixer.Fix(input);
        StringAssert.Contains("<sprite name=\"coin\">", result);
    }

    [Test]
    public void RichTextTagsSurviveUnshaped()
    {
        string input = "<color=#FF0000>\u0645\u0631\u062D\u0628\u0627</color> Bold";
        string result = ArabicFixer.Fix(input);
        StringAssert.Contains("<color=#FF0000>", result);
        StringAssert.Contains("</color>", result);
    }

    [Test]
    public void MultiLine_LinesStayInTopToBottomOrder()
    {
        string input = "line one \u0639\u0631\u0628\u064A\nline two \u0639\u0631\u0628\u064A\nline three";
        string result = ArabicFixer.Fix(input);
        string[] lines = result.Split('\n');
        Assert.AreEqual(3, lines.Length);
        StringAssert.StartsWith("line three", lines[2]); // third logical line still last
        StringAssert.Contains("one", lines[0]);
        StringAssert.Contains("two", lines[1]);
    }

    [Test]
    public void LamAlefLigature_ProducesSingleGlyph()
    {
        // "لا" (laa) in isolation should become the single ligature glyph FEFB
        string input = "\u0644\u0627";
        string expected = "\uFEFB";
        Assert.AreEqual(expected, ArabicFixer.Fix(input));
    }

    [Test]
    public void ParenthesesAreMirroredForRtlContext()
    {
        // "اسمي أحمد (Ahmed)" -> parentheses should still visually "open" before Ahmed
        string input = "\u0627\u0633\u0645\u064A \u0623\u062D\u0645\u062F (Ahmed)";
        string result = ArabicFixer.Fix(input);
        int openIndex = result.IndexOf('(');
        int closeIndex = result.IndexOf(')');
        int ahmedIndex = result.IndexOf("Ahmed");
        Assert.Less(openIndex, ahmedIndex);
        Assert.Less(ahmedIndex, closeIndex);
    }

    [Test]
    public void EmptyAndNullInput_DoesNotThrow()
    {
        Assert.AreEqual(string.Empty, ArabicFixer.Fix(string.Empty));
        Assert.IsNull(ArabicFixer.Fix(null));
    }

    // --- Regression tests for the "English word order gets reversed" bug ---
    // (FixLine used to reverse the entire flat token list, which correctly
    // repositioned Arabic vs. Latin blocks but ALSO flipped the relative
    // order of multiple Latin/Number tokens within the same line.)

    [Test]
    public void MultipleEnglishWords_KeepTheirRelativeOrder()
    {
        // "and it" must stay "and it", never "it and", even though the line
        // also contains Arabic and must be visually reordered overall.
        string input = "\u0645\u0631\u062D\u0628\u0627 and it";
        string result = ArabicFixer.Fix(input);
        Assert.Less(result.IndexOf("and"), result.IndexOf("it"),
            "'and' must still appear before 'it' - English word order must not be reversed");
    }

    [Test]
    public void TwoLatinWordsSeparatedBySpace_StayInOrder_EvenNextToArabic()
    {
        // "line one" (two Latin words) sitting next to an Arabic word must not
        // become "one line" - only the Arabic/Latin BLOCKS get repositioned,
        // not the individual words inside an LTR block.
        string input = "line one \u0639\u0631\u0628\u064A";
        string result = ArabicFixer.Fix(input);
        Assert.Less(result.IndexOf("line"), result.IndexOf("one"),
            "'line' must still appear before 'one'");
    }

    [Test]
    public void WordsDoNotCollideAtRtlLtrBoundary()
    {
        // The space between an Arabic word and an adjacent English word must
        // survive reordering - it must not vanish (words jammed together)
        // nor end up detached at the far end of the string.
        string input = "Hello \u0645\u0631\u062D\u0628\u0627 World";
        string result = ArabicFixer.Fix(input);
        StringAssert.DoesNotContain("HelloWorld", result);
        int helloIdx = result.IndexOf("Hello");
        int worldIdx = result.IndexOf("World");
        Assert.AreNotEqual(-1, helloIdx);
        Assert.AreNotEqual(-1, worldIdx);
        // Whatever sits immediately next to "Hello" and to "World" must be a
        // space, not another letter (i.e. no word is glued to Hello/World).
        Assert.IsTrue(char.IsWhiteSpace(result[helloIdx + "Hello".Length]));
        Assert.IsTrue(char.IsWhiteSpace(result[worldIdx - 1]));
    }

    // --- Regression tests for the "harakah shifts / disappears" bug ---
    // A Lam-Alef ligature ("لا") used to silently DROP any diacritic sitting
    // on the alef, since only the lam's own marks were re-emitted before the
    // shaper jumped straight past the alef's cluster.

    [Test]
    public void DiacriticOnLigatureAlef_IsNotDropped()
    {
        // "كلاَم" - the alef in "لا" carries a fatha (U+064E). It must still
        // be present in the output somewhere, attached next to the ligature
        // glyph, not silently discarded.
        string input = "\u0643\u0644\u0627\u064E\u0645"; // ك ل ا َ م
        string result = ArabicFixer.Fix(input);
        StringAssert.Contains("\u064E", result);
    }

    [Test]
    public void TagBetweenArabicLetters_DoesNotOrphanNeighboringContent()
    {
        // A TMP tag (like <sprite>) sitting between two Arabic words must
        // not corrupt or drop the surrounding Arabic text, and the tag's own
        // markup must survive completely intact (never split/reversed
        // character-by-character).
        string input = "\u0627\u0644\u0643\u0644\u0627\u0645<sprite name=\"pawn\"> \u0647\u0646\u0627"; // الكلام<sprite name="pawn"> هنا
        string result = ArabicFixer.Fix(input);
        StringAssert.Contains("<sprite name=\"pawn\">", result);
    }

    [Test]
    public void DiacriticOnLastLetter_ReadsAfterThatLetter_NotBeforeIt()
    {
        // "مربعٌ" - tanween (dammatan, U+064C) sits on the LAST letter (ain).
        // TMP draws left-to-right with no bidi/GPOS repositioning, so the
        // OUTPUT STRING's storage order IS the physical on-screen order.
        // Reading that physical layout right-to-left (real Arabic reading
        // direction) must place the mark immediately AFTER ain's shaped
        // glyph - which means the mark must be the very FIRST character
        // written in the output string (since ain, being the last logical
        // letter, is reversed to the visual start, and its mark - read even
        // later than ain itself - must sit one step further toward the
        // visual start still, i.e. before it in storage order).
        string input = "\u0645\u0631\u0628\u0639\u064C"; // م ر ب ع ٌ
        string result = ArabicFixer.Fix(input);
        Assert.AreEqual('\u064C', result[0],
            "the tanween must be the first character of the output, immediately preceding ain's glyph - not stranded after beh's glyph");
    }

    // --- Regression tests for the "wrapping tag around multiple words gets
    // its open/close halves swapped" bug ---
    // A tag wrapping an entire multi-word RTL span used to have its opening
    // and closing tags end up on the WRONG ends after word-order reversal,
    // since each half stayed glued to whichever word originally sat at that
    // edge, and reversal swapped those words' positions right along with
    // their attached tag halves.

    [Test]
    public void WrappingTagAroundMultipleArabicWords_KeepsOpenBeforeClose()
    {
        string input = "<align=\"center\">\u064A\u062A\u062D\u0631\u0643 \u0645\u0631\u0628\u0639</align>"; // <align="center">يتحرك مربع</align>
        string result = ArabicFixer.Fix(input);
        int openIdx = result.IndexOf("<align=\"center\">");
        int closeIdx = result.LastIndexOf("</align>");
        Assert.AreNotEqual(-1, openIdx);
        Assert.AreNotEqual(-1, closeIdx);
        Assert.Less(openIdx, closeIdx, "the opening tag must still come before the closing tag after reversal");
    }

    [Test]
    public void WrappingTagAroundWordWithTrailingArabicPunctuation_StaysIntact()
    {
        // The word carries a trailing Arabic comma ("،", classified as
        // Neutral, not Arabic) glued directly onto it with no space - this
        // used to break the tag-duplication chain right before reaching the
        // closing tag.
        string input = "<align=\"center\">\u0644\u0644\u0623\u0645\u0627\u0645\u060C \u0648\u064A\u0642\u0636\u064A</align>"; // <align="center">للأمام، ويقضي</align>
        string result = ArabicFixer.Fix(input);
        StringAssert.Contains("<align=\"center\">", result);
        StringAssert.Contains("</align>", result);
        Assert.IsTrue(result.IndexOf("<align=\"center\">") < result.LastIndexOf("</align>"));
    }

    [Test]
    public void WrappingTagAroundEmbeddedNumber_IsNotReversedIntoWrongDigitOrder()
    {
        // A bare number wrapped in its own duplicated tag pair, inside an
        // Arabic sentence, must never have its OWN digit order reversed
        // (e.g. "45" must never become "54") - reversal must only ever
        // apply to genuinely Arabic content.
        string input = "\u0628\u0632\u0627\u0648\u064A\u0629 <align=\"center\">45</align> \u062F\u0631\u062C\u0629"; // بزاوية <align="center">45</align> درجة
        string result = ArabicFixer.Fix(input);
        StringAssert.Contains("<align=\"center\">45</align>", result);
    }

    // --- Regression tests for the "conservative" tag handling redesign ---
    // A tag wrapping several words that all fit on ONE physical line must
    // come out as exactly ONE open + ONE close, in the originally-authored
    // order, with only the CONTENT inside correctly reordered - never
    // duplicated per word.

    [Test]
    public void MultiWordTagOnOneLine_ProducesExactlyOnePairInCorrectOrder()
    {
        string input = "<align=\"right\">\u0645\u062D\u0645\u062F \u0644\u0635 \u0643\u064A \u0645\u0635\u0631</align>"; // <align="right">محمد لص كي مصر</align>
        string result = ArabicFixer.Fix(input);
        int openCount = 0, idx = 0;
        while ((idx = result.IndexOf("<align=\"right\">", idx)) != -1) { openCount++; idx++; }
        int closeCount = 0; idx = 0;
        while ((idx = result.IndexOf("</align>", idx)) != -1) { closeCount++; idx++; }
        Assert.AreEqual(1, openCount, "exactly one opening tag - no per-word duplication");
        Assert.AreEqual(1, closeCount, "exactly one closing tag - no per-word duplication");
        Assert.Less(result.IndexOf("<align=\"right\">"), result.IndexOf("</align>"));
    }

    [Test]
    public void PureEnglishMultiWordTag_IsLeftCompletelyUnchanged()
    {
        // A tag wrapping several English words with no Arabic inside at all
        // must be returned byte-for-byte identical - no reversal, no
        // reshaping, no duplication.
        string input = "<align=\"right\">apple is ded</align>";
        string result = ArabicFixer.Fix(input);
        Assert.AreEqual(input, result);
    }

    [Test]
    public void NestedTags_BothPreserveExactlyOnePairEach()
    {
        string input = "<b><align=\"right\">\u0645\u0631\u062D\u0628\u0627</align></b>"; // <b><align="right">مرحبا</align></b>
        string result = ArabicFixer.Fix(input);
        StringAssert.StartsWith("<b><align=\"right\">", result);
        StringAssert.EndsWith("</align></b>", result);
    }

    // --- Tests for NormalizeReversedTags (the "Reverse Tags" toggle) ---

    [Test]
    public void NormalizeReversedTags_SimpleReversedPair_BecomesStandardOrder()
    {
        string input = "</align>\u0645\u062D\u0645\u062F<align=\"right\">"; // </align>محمد<align="right">
        string result = ArabicFixer.NormalizeReversedTags(input);
        Assert.AreEqual("<align=\"right\">\u0645\u062D\u0645\u062F</align>", result);
    }

    [Test]
    public void NormalizeReversedTags_NestedReversedPairs_BothUnnested()
    {
        string input = "</align></b>\u0645\u0631\u062D\u0628\u0627<b><align=\"right\">"; // </align></b>مرحبا<b><align="right">
        string result = ArabicFixer.NormalizeReversedTags(input);
        Assert.AreEqual("<align=\"right\"><b>\u0645\u0631\u062D\u0628\u0627</b></align>", result);
    }

    [Test]
    public void NormalizeReversedTags_VoidTagInside_LeftUntouched()
    {
        string input = "</align>hello<sprite name=\"x\"> world<align=\"right\">";
        string result = ArabicFixer.NormalizeReversedTags(input);
        Assert.AreEqual("<align=\"right\">hello<sprite name=\"x\"> world</align>", result);
    }

    [Test]
    public void NormalizeReversedTags_ThenFix_ProducesCorrectSinglePair()
    {
        string input = "</align>\u0645\u062D\u0645\u062F \u0644\u0635 \u0643\u064A<align=\"right\">"; // </align>محمد لص كي<align="right">
        string normalized = ArabicFixer.NormalizeReversedTags(input);
        string result = ArabicFixer.Fix(normalized);
        Assert.AreEqual(1, CountOccurrences(result, "<align=\"right\">"));
        Assert.AreEqual(1, CountOccurrences(result, "</align>"));
        Assert.Less(result.IndexOf("<align=\"right\">"), result.IndexOf("</align>"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx)) != -1) { count++; idx += needle.Length; }
        return count;
    }
}
