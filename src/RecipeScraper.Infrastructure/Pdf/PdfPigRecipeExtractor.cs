using RecipeScraper.Core;
using RecipeScraper.Core.Abstractions;
using RecipeScraper.Infrastructure.Ocr;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace RecipeScraper.Infrastructure.Pdf;

/// <summary>Extracts every recipe out of a born-digital cookbook PDF built on a fixed two-column template
/// (a full-width title/blurb block, then an "ingredients" column and an "instructions" column side by
/// side). Reconstructs each page's text in proper reading order from PdfPig's line/word position data —
/// the library's own reading-order detector interleaves the two columns line-by-line, so this splits by
/// each line's horizontal position instead (see <see cref="PageTextReconstructor"/>) — then hands the
/// reconstructed text to the same <see cref="RecipeTextParser"/> the OCR path uses. A recipe that spans
/// more than one PDF page (the second page holding only continuing instructions, no new "ingredients"
/// header) is folded into the preceding recipe rather than treated as its own entry.</summary>
public sealed class PdfPigRecipeExtractor(RecipeTextParser textParser) : IRecipeDocumentParser
{
    public Task<IReadOnlyList<Recipe>> ParseAllAsync(byte[] documentBytes, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(documentBytes);
        var recipes = new List<Recipe>();
        var currentRecipePages = new List<string>();

        void FinalizeCurrentRecipe()
        {
            if (currentRecipePages.Count == 0) return;
            recipes.Add(textParser.Parse(string.Join("\n\n", currentRecipePages)));
            currentRecipePages.Clear();
        }

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reconstruction = PageTextReconstructor.Reconstruct(ExtractLines(page));

            switch (reconstruction.Kind)
            {
                case PageKind.RecipeStart:
                    FinalizeCurrentRecipe();
                    currentRecipePages.Add(reconstruction.Text);
                    break;
                case PageKind.Continuation when currentRecipePages.Count > 0:
                    currentRecipePages.Add(reconstruction.Text);
                    break;
                case PageKind.Continuation:
                case PageKind.Skip:
                    break;
            }
        }
        FinalizeCurrentRecipe();

        return Task.FromResult<IReadOnlyList<Recipe>>(recipes);
    }

    private static List<PositionedLine> ExtractLines(Page page)
    {
        var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
        var blocks = DefaultPageSegmenter.Instance.GetBlocks(words);
        return blocks
            .SelectMany(b => b.TextLines)
            .Select(l => new PositionedLine(l.BoundingBox.Left, l.BoundingBox.Top, l.Text))
            .ToList();
    }
}
