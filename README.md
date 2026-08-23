# Arabic BeTTer RTL for unity

A small, dependency-free plugin that makes TextMeshPro display mixed
Arabic/English text correctly — proper letter joining, right-to-left word
order, and safe handling of numbers, `<sprite>` tags, and TMP rich-text tags.

- Arabic letters are shaped into correct joined forms (isolated / initial /
  medial / final), including Lam-Alef ligatures and diacritics.
- Arabic reorders right-to-left; English words and numbers in the same line
  stay left-to-right and are never scrambled.
- `<sprite>` and other TMP tags are treated as atomic — never shaped or
  split, just correctly repositioned.
- Multi-line text keeps its original top-to-bottom line order.

## Install

1. Copy `Core/ArabicFixer.cs` and `Unity/RTLTextMeshPro.cs` anywhere under
   `Assets/` in your project.
2. Make sure **TextMeshPro** is imported (Window ▸ TextMeshPro ▸ Import TMP
   Essential Resources).
3. *(Optional)* copy `Tests/ArabicFixerTests.cs` if you want the automated
   test suite (needs the `com.unity.test-framework` package).

### ⚠️ Font requirement

This plugin outputs Arabic **Presentation Form** code points
(`U+FE70–U+FEFF`), since that's how a non-shaping renderer like TMP gets
correctly-joined glyphs. When generating your Font Asset (Window ▸
TextMeshPro ▸ Font Asset Creator), set **Character Set** to *Unicode Range
(Hex)* and include:

```
0600-06FF, FB50-FDFF, FE70-FEFF
```

Use a source font that actually contains Arabic presentation forms (Noto
Sans Arabic, Dubai, Cairo, Amiri, etc.). Tofu/empty boxes almost always
means a missing glyph range, not a shaping bug.

## Usage (Editor)

1. Add `RTLTextMeshPro` to the same GameObject as your `TMP_Text` component.
2. Type text into **Source Text** in normal logical order — mixed
   Arabic/English/numbers/tags is fine:
   ```
   مرحباً <sprite name="coin"> Hello، لديك 5 عملات!
   ```
3. Don't edit the TMP component's own Text field directly — it gets
   overwritten on every apply.

**Long/wrapping text:** tick **Rtl Wrap** on the component instead of
relying on TMP's own word-wrap — TMP wraps by raw character position, which
scrambles line order once text has been RTL-reordered. Rtl Wrap measures
against the actual shaped glyph widths and breaks lines *before*
reordering, so line order always stays correct. Turn on **Fixed Wrap** too
if you want a stable wrap width regardless of the box's live size.

### Tags spanning multiple words or lines

A tag wrapping several words (`<align="right">word1 word2</align>`) comes
out as exactly one open + one close, in the order you wrote them — the
content inside gets reordered, the tag itself never does. If Rtl Wrap
breaks that span across physical lines, only the lines it actually touches
get a copy (closed at the end, reopened at the start of the next), not
every word.

**Reverse Tags:** some RTL-aware editors store tags backwards —
`</align>content<align="right">` instead of `<align="right">content</align>` —
because the closing marker gets typed before the opening one. Tick
**Reverse Tags** if that's how your source text looks, and it's normalized
to standard order automatically (nested reversed tags too).

**Align Right:** sets the text's default alignment — ON = right, OFF = left.
Only changes horizontal alignment; Top/Middle/Bottom is left alone. An
inline `<align=...>` tag in the text still overrides this for its own span.
**Always Right** is a lock underneath it: when ON, it forces right
regardless of what Align Right is set to.

## Usage (code) — switching language at runtime

The whole point of driving this through code is that a language change is
just "push new source text through the component" — the plugin auto-detects
per line whether it's RTL, LTR, or mixed, so the same call works for Arabic,
English, or anything in between. No mode switching needed.

```csharp
using ArabicUnityRTL.Unity;

public class LocalizedLabel : MonoBehaviour
{
    [SerializeField] private RTLTextMeshPro label;
    [SerializeField] private string localizationKey;

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += Refresh;
        Refresh(LocalizationManager.CurrentLanguage);
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= Refresh;
    }

    private void Refresh(string languageCode)
    {
        string raw = LocalizationManager.GetString(localizationKey, languageCode);
        label.SetText(raw); // shapes + reorders (if needed) + displays immediately
    }
}
```

If you're wrapping text and driving the width from your own code instead of
the component's Rtl Wrap toggle, use `SetTextWrapped` instead:

```csharp
label.SetTextWrapped(raw, maxWidthInLocalUnits);
```

That's the entire integration surface — one call per text update, whichever
language it's in.

### Optional: Arabic-Indic digits

Tick **Use Arabic Indic Digits** to display `0123` as `٠١٢٣` in visible
text. Digits inside tags (`<sprite=2>`, `<size=38>`, `<color=#FF0000>`) are
never touched, so tag syntax can't break.

## API

```csharp
namespace ArabicUnityRTL.Core {
    public static class ArabicFixer {
        public static string Fix(string input); // shape + reorder a (possibly multi-line) string
        public static string NormalizeReversedTags(string input); // fix backwards </tag>...<tag> pairs
    }
}

namespace ArabicUnityRTL.Unity {
    public class RTLTextMeshPro : MonoBehaviour {
        public void SetText(string raw);
        public void SetTextWrapped(string raw, float maxWidth);
    }
}
```

## Known limitations

- Implements the simplified, practical subset of bidi behavior most Unity
  Arabic plugins use (per-line direction detection + run reversal), not the
  full Unicode Bidirectional Algorithm (UAX #9).
- Ligatures beyond Lam-Alef aren't special-cased.
- `SetTextWrapped` / Rtl Wrap width measurement is a close approximation of
  TMP's internal line-breaker, not pixel-identical.
- Kashida (justification stretching) isn't implemented.

## License

MIT — see [LICENSE](LICENSE).
