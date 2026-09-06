namespace RecipeScraper.Infrastructure.Pdf;

public enum PageKind { RecipeStart, Continuation, Skip }

public readonly record struct PositionedLine(double Left, double Top, string Text);

public readonly record struct PageReconstruction(PageKind Kind, string Text);

/// <summary>Pure, PdfPig-independent reconstruction of a two-column cookbook page's text from its lines'
/// positions — kept separate from <see cref="PdfPigRecipeExtractor"/> (which gets those positioned lines
/// out of an actual PdfPig <c>Page</c>) so this logic is directly unit-testable with hand-built input,
/// rather than at the mercy of PdfPig's own page-segmentation heuristics on a contrived test fixture.
/// See <see cref="PdfPigRecipeExtractor"/>'s doc comment for the algorithm this implements.</summary>
public static class PageTextReconstructor
{
    // A page whose non-footer text is shorter than this is a table-of-contents/section-divider/blank
    // page, not a recipe continuation — there's no reliable structural marker for those pages, so length
    // is the only signal available.
    public const int MinContinuationTextLength = 120;

    // A real gap between two columns' left edges is large; anything smaller is just natural word-start
    // jitter within a single column.
    private const double ColumnGapThreshold = 40;

    // Page-number footers sit in the bottom margin, comfortably below any real body text.
    private const double FooterTopThreshold = 40;

    public static PageReconstruction Reconstruct(IReadOnlyList<PositionedLine> allLines)
    {
        var contentLines = allLines
            .Where(l => !(l.Top < FooterTopThreshold && IsAllDigits(l.Text)))
            .ToList();

        var headerMarkerLines = contentLines.Where(l => IsHeaderMarkerLine(l.Text)).ToList();

        if (headerMarkerLines.Count > 0)
        {
            // Everything above the column headers is the title/blurb preamble; everything below (minus
            // the header-marker lines themselves) is the two columns' content.
            var preambleBoundary = headerMarkerLines.Max(l => l.Top);
            var preamble = contentLines
                .Where(l => l.Top > preambleBoundary)
                .OrderByDescending(l => l.Top)
                .Select(l => l.Text);

            var columnLines = contentLines
                .Where(l => l.Top < preambleBoundary && !IsHeaderMarkerLine(l.Text))
                .ToList();
            var (left, right) = SplitIntoColumns(columnLines);

            var lines = new List<string>();
            lines.AddRange(preamble);
            lines.Add("ingredients");
            lines.AddRange(left.OrderByDescending(l => l.Top).Select(l => l.Text));
            lines.Add("instructions");
            lines.AddRange(right.OrderByDescending(l => l.Top).Select(l => l.Text));

            return new PageReconstruction(PageKind.RecipeStart, string.Join("\n", lines));
        }

        var combinedLength = contentLines.Sum(l => l.Text.Length);
        if (combinedLength < MinContinuationTextLength)
        {
            return new PageReconstruction(PageKind.Skip, "");
        }

        var singleColumnText = string.Join("\n", contentLines.OrderByDescending(l => l.Top).Select(l => l.Text));
        return new PageReconstruction(PageKind.Continuation, singleColumnText);
    }

    // Not a general-purpose N-column detector — just splits a two-column page's remaining content lines
    // by finding the single biggest horizontal gap between distinct left-edge positions. Falls back to
    // treating everything as one column if no gap large enough to be a real gutter is found.
    private static (List<PositionedLine> Left, List<PositionedLine> Right) SplitIntoColumns(
        List<PositionedLine> lines)
    {
        if (lines.Count == 0) return (lines, []);

        var distinctLefts = lines.Select(l => l.Left).Distinct().OrderBy(x => x).ToList();
        var splitPoint = double.MaxValue;
        var biggestGap = 0.0;
        for (var i = 1; i < distinctLefts.Count; i++)
        {
            var gap = distinctLefts[i] - distinctLefts[i - 1];
            if (gap > biggestGap)
            {
                biggestGap = gap;
                splitPoint = (distinctLefts[i] + distinctLefts[i - 1]) / 2;
            }
        }

        if (biggestGap < ColumnGapThreshold) return (lines, []);

        var left = lines.Where(l => l.Left < splitPoint).ToList();
        var right = lines.Where(l => l.Left >= splitPoint).ToList();
        return (left, right);
    }

    private static bool IsAllDigits(string text) => text.Length > 0 && text.All(char.IsDigit);

    // This book's design deliberately letter-spaces section headers (e.g. "i n g r e d i e n t s"), and
    // depending on how the page segmenter grouped the two headers they can land as one merged line or two
    // separate ones — stripping whitespace before comparing handles every shape.
    private static bool IsHeaderMarkerLine(string text)
    {
        var compact = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return compact.Equals("ingredients", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("instructions", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("ingredientsinstructions", StringComparison.OrdinalIgnoreCase);
    }
}
