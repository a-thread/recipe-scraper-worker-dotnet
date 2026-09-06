using RecipeScraper.Infrastructure.Pdf;

namespace RecipeScraper.Tests;

public class PageTextReconstructorTests
{
    [Fact]
    public void ReconstructsATwoColumnRecipePageInReadingOrder()
    {
        // Mirrors the real layout: title, then "ingredients"/"instructions" column headers at the same
        // row, then two columns of content at consistent left-edge positions, then a footer page number.
        var lines = new List<PositionedLine>
        {
            new(58, 700, "blueberry muffins"),
            new(58, 420, "ingredients instructions"), // merged header row, as PdfPig's segmenter sometimes emits it
            new(58, 400, "1 cup flour"),
            new(236, 400, "Preheat the oven."),
            new(58, 385, "1 cup sugar"),
            new(236, 385, "Mix well."),
            new(561, 20, "5"), // footer page number
        };

        var result = PageTextReconstructor.Reconstruct(lines);

        Assert.Equal(PageKind.RecipeStart, result.Kind);
        Assert.Equal(
            "blueberry muffins\ningredients\n1 cup flour\n1 cup sugar\ninstructions\nPreheat the oven.\nMix well.",
            result.Text);
    }

    [Fact]
    public void HandlesSeparateIngredientsAndInstructionsHeaderLines()
    {
        // Sometimes the two header labels land as two separate TextLines instead of one merged line.
        var lines = new List<PositionedLine>
        {
            new(46, 550, "pastel des tres leches"),
            new(46, 444, "ingredients"),
            new(232, 443, "instructions"),
            new(46, 402, "1 stick butter"),
            new(232, 417, "Preheat oven."),
        };

        var result = PageTextReconstructor.Reconstruct(lines);

        Assert.Equal(PageKind.RecipeStart, result.Kind);
        Assert.Contains("1 stick butter", result.Text);
        Assert.Contains("Preheat oven.", result.Text);
        // The ingredients-column line must precede the instructions-column line in the reconstructed text.
        Assert.True(result.Text.IndexOf("1 stick butter") < result.Text.IndexOf("Preheat oven."));
    }

    [Fact]
    public void DropsTheFooterPageNumberEvenWhenItSharesAColumnsLeftEdge()
    {
        var lines = new List<PositionedLine>
        {
            new(58, 700, "Title"),
            new(58, 420, "ingredients"),
            new(236, 420, "instructions"),
            new(58, 400, "1 cup flour"),
            new(58, 20, "12"), // footer, same left edge as the ingredients column
        };

        var result = PageTextReconstructor.Reconstruct(lines);

        Assert.DoesNotContain("12", result.Text.Split('\n'));
    }

    [Fact]
    public void ClassifiesASubstantialSingleColumnPageWithNoHeadersAsContinuation()
    {
        var longText = string.Concat(Enumerable.Repeat("More instructions continue here. ", 5));
        var lines = new List<PositionedLine>
        {
            new(32, 500, longText),
            new(557, 20, "41"), // footer
        };

        var result = PageTextReconstructor.Reconstruct(lines);

        Assert.Equal(PageKind.Continuation, result.Kind);
        Assert.Contains("More instructions continue here.", result.Text);
        Assert.DoesNotContain("41", result.Text);
    }

    [Fact]
    public void ClassifiesASparseNoHeaderPageAsSkip()
    {
        var lines = new List<PositionedLine>
        {
            new(200, 400, "main dishes"),
            new(561, 20, "22"),
        };

        var result = PageTextReconstructor.Reconstruct(lines);

        Assert.Equal(PageKind.Skip, result.Kind);
    }

    [Fact]
    public void TreatsContentAsOneColumnWhenNoRealColumnGapExists()
    {
        // All lines clustered closely together horizontally — no real two-column layout, despite an
        // "ingredients"/"instructions" pair being present (defensive fallback, not expected in practice).
        var lines = new List<PositionedLine>
        {
            new(50, 700, "Title"),
            new(50, 420, "ingredients"),
            new(55, 419, "instructions"),
            new(50, 400, "1 cup flour"),
            new(52, 385, "2 cups sugar"),
        };

        var result = PageTextReconstructor.Reconstruct(lines);

        Assert.Equal(PageKind.RecipeStart, result.Kind);
        Assert.Contains("1 cup flour", result.Text);
        Assert.Contains("2 cups sugar", result.Text);
    }
}
