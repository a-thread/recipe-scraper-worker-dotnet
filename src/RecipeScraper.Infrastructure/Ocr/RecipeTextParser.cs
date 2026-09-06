using System.Text.RegularExpressions;
using RecipeScraper.Core;

namespace RecipeScraper.Infrastructure.Ocr;

/// <summary>Heuristically extracts a <see cref="Recipe"/> out of plain recipe-page text — one or more
/// pages, concatenated in page order — regardless of where that text came from (OCR output, or
/// PDF-reconstructed text). The text-cleanup logic here (fraction-glyph normalization, checkbox-glyph
/// stripping, etc.) intentionally mirrors — rather than reuses — the equivalent private logic in
/// <c>AngleSharpRecipeParser</c>, since that file is an in-progress, uncommitted refactor at the time this
/// was written; once that refactor lands, the shared pieces are a good candidate to de-duplicate.
/// Pure and stateless — takes a string, returns a <see cref="Recipe"/>, no I/O.</summary>
public sealed partial class RecipeTextParser
{
    private const int MaxGroupHeadingLength = 40;

    private static readonly Dictionary<char, string> FractionMap = new()
    {
        ['½'] = "1/2", ['⅓'] = "1/3", ['⅔'] = "2/3", ['¼'] = "1/4", ['¾'] = "3/4",
        ['⅕'] = "1/5", ['⅖'] = "2/5", ['⅗'] = "3/5", ['⅘'] = "4/5",
    };

    public Recipe Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        var ingredientsIdx = lines.FindIndex(l => IngredientsHeaderRegex().IsMatch(l.Trim()));
        var instructionsSearchStart = ingredientsIdx >= 0 ? ingredientsIdx + 1 : 0;
        var instructionsIdx = lines.FindIndex(instructionsSearchStart, l => InstructionsHeaderRegex().IsMatch(l.Trim()));

        var ingredients = ingredientsIdx < 0
            ? []
            : ExtractIngredients(lines, ingredientsIdx + 1, instructionsIdx >= 0 ? instructionsIdx : lines.Count);

        var steps = instructionsIdx < 0
            ? []
            : ExtractSteps(lines, instructionsIdx + 1, lines.Count);

        var preambleEnd = ingredientsIdx >= 0 ? ingredientsIdx : (instructionsIdx >= 0 ? instructionsIdx : lines.Count);

        return new Recipe
        {
            Title = ExtractTitle(lines, preambleEnd),
            Description = "",
            ImgUrl = "",
            PrepTime = ExtractDurationMinutes(text, PrepTimeLabelRegex()),
            CookTime = ExtractDurationMinutes(text, CookTimeLabelRegex()),
            Servings = ExtractServings(text),
            Ingredients = ingredients,
            Steps = steps,
            OriginalRecipeUrl = "",
        };
    }

    // Only searches the preamble before the ingredients/instructions header — a substantive line found
    // *after* that boundary belongs to a section, not the title, so it must not be mistaken for one. A
    // title that wraps onto further lines — including across a blank line, per this cookbook's centered
    // layout splitting e.g. "spinach + walnut crumble" / "gnocchi" into separate paragraphs — is joined
    // into one string. Three kinds of line must NOT be swept into that join: a prep/cook-time or servings
    // label (ends the title outright — a metadata block follows); a letter-spaced section "eyebrow" label
    // (e.g. "s o u p s" above the first recipe of a section) — skipped, since the real title still
    // follows it on the same page; and a personal blurb/story paragraph, which — unlike a short wrapped
    // title fragment — reads as a full sentence, so it also ends the title outright.
    private static string ExtractTitle(List<string> lines, int preambleEnd)
    {
        var titleLines = new List<string>();
        for (var i = 0; i < preambleEnd && i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (PageNumberOnlyRegex().IsMatch(line)) continue;
            if (IsMetadataLine(line)) break;
            if (IsLetterSpacedLabel(line)) continue;
            if (LooksLikeSentence(line)) break;
            titleLines.Add(line);
        }
        return titleLines.Count > 0 ? string.Join(" ", titleLines) : "Untitled Recipe";
    }

    // A stylized section-eyebrow label (e.g. "s o u p s", "p a n t r y / s a u c e s") has a space after
    // almost every character — a real title/phrase, even a short one, doesn't.
    private static bool IsLetterSpacedLabel(string line)
    {
        var nonSpaceCount = line.Count(c => !char.IsWhiteSpace(c));
        var spaceCount = line.Length - nonSpaceCount;
        return nonSpaceCount > 0 && spaceCount >= nonSpaceCount - 2;
    }

    // Titles and their wrapped continuations are short phrases; a blurb sentence reads much longer even
    // before accounting for the rest of the paragraph that follows it.
    private static bool LooksLikeSentence(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 8;

    private static bool IsMetadataLine(string line) =>
        PrepTimeLabelRegex().IsMatch(line) || CookTimeLabelRegex().IsMatch(line) || ServingsRegex().IsMatch(line);

    private static List<StepIngredient> ExtractIngredients(List<string> lines, int start, int end)
    {
        var result = new List<StepIngredient>();
        string? currentGroup = null;

        for (var i = start; i < end && i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (PageNumberOnlyRegex().IsMatch(line)) continue;

            if (IsGroupHeading(line))
            {
                currentGroup = line.TrimEnd(':', ' ');
                continue;
            }

            var normalized = NormalizeIngredientLine(line);
            if (normalized.Length == 0) continue;
            result.Add(new StepIngredient(Guid.NewGuid().ToString(), normalized, currentGroup));
        }

        return result;
    }

    // A short, colon-terminated, digit-free line (e.g. "For the topping:") is a sub-heading, not an
    // ingredient — mirrors the same heuristic the HTML-based scraper uses for WPRM-style groupings.
    private static bool IsGroupHeading(string line) =>
        line.EndsWith(':') && line.Length <= MaxGroupHeadingLength && !line.Any(char.IsDigit);

    private static List<StepIngredient> ExtractSteps(List<string> lines, int start, int end)
    {
        var count = Math.Max(0, Math.Min(end, lines.Count) - start);
        // A trailing page number (e.g. a lone "22" after the last paragraph) would otherwise be picked up
        // as a spurious final step by both the blank-line-paragraph and one-step-per-line fallbacks below.
        var sectionLines = lines.GetRange(Math.Min(start, lines.Count), count)
            .Where(l => !PageNumberOnlyRegex().IsMatch(l.Trim()))
            .ToList();

        var numberedSteps = ExtractNumberedSteps(sectionLines);
        if (numberedSteps.Count > 0) return numberedSteps;

        var paragraphs = Regex.Split(string.Join("\n", sectionLines), @"\n\s*\n")
            .Select(p => WhitespaceRunRegex().Replace(p, " ").Trim())
            .Where(p => p.Length > 0)
            .ToList();
        if (paragraphs.Count > 1)
        {
            return paragraphs.Select(p => new StepIngredient(Guid.NewGuid().ToString(), p)).ToList();
        }

        return sectionLines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => new StepIngredient(Guid.NewGuid().ToString(), l))
            .ToList();
    }

    // OCR on printed cookbook pages routinely keeps the "1.", "2)" step markers but drops blank lines
    // between steps, so numbered markers are checked first, before falling back to blank-line paragraphs.
    private static List<StepIngredient> ExtractNumberedSteps(List<string> sectionLines)
    {
        var steps = new List<StepIngredient>();
        var current = new List<string>();

        void Flush()
        {
            if (current.Count == 0) return;
            var text = WhitespaceRunRegex().Replace(string.Join(" ", current), " ").Trim();
            if (text.Length > 0) steps.Add(new StepIngredient(Guid.NewGuid().ToString(), text));
            current.Clear();
        }

        foreach (var rawLine in sectionLines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var marker = StepMarkerRegex().Match(line);
            if (marker.Success)
            {
                Flush();
                current.Add(line[marker.Length..].Trim());
            }
            else if (current.Count > 0)
            {
                current.Add(line);
            }
        }
        Flush();

        return steps;
    }

    private static int ExtractDurationMinutes(string text, Regex labelRegex)
    {
        var match = labelRegex.Match(text);
        if (!match.Success) return 0;
        var fragment = match.Groups[1].Value;

        var minutes = 0;
        var foundUnit = false;
        foreach (var hourMatch in HoursRegex().Matches(fragment).Cast<Match>())
        {
            if (int.TryParse(hourMatch.Groups[1].Value, out var hours))
            {
                minutes += hours * 60;
                foundUnit = true;
            }
        }
        foreach (var minuteMatch in MinutesRegex().Matches(fragment).Cast<Match>())
        {
            if (int.TryParse(minuteMatch.Groups[1].Value, out var mins))
            {
                minutes += mins;
                foundUnit = true;
            }
        }
        if (foundUnit) return minutes;

        var bareMatch = BareNumberRegex().Match(fragment);
        return bareMatch.Success && int.TryParse(bareMatch.Groups[1].Value, out var bareMinutes) ? bareMinutes : 0;
    }

    private static int ExtractServings(string text)
    {
        var match = ServingsRegex().Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var servings) ? servings : 0;
    }

    private static string NormalizeIngredientLine(string raw)
    {
        var text = CheckboxGlyphRegex().Replace(raw, "");
        text = WhitespaceRunRegex().Replace(text, " ");
        text = NumberFractionRegex().Replace(text, "$1 $2");
        text = FractionCharRegex().Replace(text, m => FractionMap[m.Value[0]]);
        text = NumberAlphaRegex().Replace(text, "$1 $2");
        return text.Trim();
    }
}
