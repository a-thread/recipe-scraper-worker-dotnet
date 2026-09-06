using RecipeScraper.Core.Abstractions;

namespace RecipeScraper.Core.UseCases;

public sealed class ImportRecipesFromPdfUseCase(IRecipeDocumentParser parser)
{
    public const int MaxPdfBytes = 20 * 1024 * 1024;
    public const string ExpectedMediaType = "application/pdf";

    public async Task<ImportRecipesFromPdfResult> ExecuteAsync(
        byte[] documentBytes, string mediaType, CancellationToken cancellationToken)
    {
        if (!string.Equals(mediaType, ExpectedMediaType, StringComparison.OrdinalIgnoreCase))
        {
            return new ImportRecipesFromPdfResult.InvalidInput($"Unsupported file type: {mediaType}");
        }
        if (documentBytes.Length == 0 || documentBytes.Length > MaxPdfBytes)
        {
            return new ImportRecipesFromPdfResult.InvalidInput("The PDF must be non-empty and under 20MB");
        }

        try
        {
            var recipes = await parser.ParseAllAsync(documentBytes, cancellationToken);
            return new ImportRecipesFromPdfResult.Success(recipes);
        }
        catch (Exception err)
        {
            return new ImportRecipesFromPdfResult.ParseFailed(err.Message);
        }
    }
}
