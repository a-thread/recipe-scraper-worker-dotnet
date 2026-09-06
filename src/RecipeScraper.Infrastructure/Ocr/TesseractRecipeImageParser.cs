using System.Diagnostics;
using RecipeScraper.Core;
using RecipeScraper.Core.Abstractions;

namespace RecipeScraper.Infrastructure.Ocr;

/// <summary>Extracts a <see cref="Recipe"/> out of photographed/scanned recipe-page images by shelling out
/// to the <c>tesseract</c> CLI for OCR (no cloud API, no per-request cost) and running the result through
/// <see cref="OcrRecipeTextParser"/>. Images are processed sequentially to keep CPU/memory bounded — this
/// is a low-traffic, personal-scale endpoint, not a batch pipeline.</summary>
public sealed class TesseractRecipeImageParser(OcrRecipeTextParser textParser, string tesseractExecutable)
    : IRecipeImageParser
{
    // Allows for slow or cold-started free-tier hosting while bounding per-image processing time.
    private static readonly TimeSpan PerImageTimeout = TimeSpan.FromSeconds(60);

    public async Task<Recipe> ParseAsync(IReadOnlyList<RecipeImage> images, CancellationToken cancellationToken)
    {
        var pages = new List<string>(images.Count);
        foreach (var image in images)
        {
            pages.Add(await RunOcrAsync(image, cancellationToken));
        }

        return textParser.Parse(string.Join("\n\n", pages));
    }

    private async Task<string> RunOcrAsync(RecipeImage image, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{ExtensionFor(image.MediaType)}");
        try
        {
            await File.WriteAllBytesAsync(tempFile, image.Data, cancellationToken);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tesseractExecutable,
                    ArgumentList = { tempFile, "stdout", "-l", "eng", "--psm", "3" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            process.Start();

            // Drain both pipes concurrently with waiting for exit — required to avoid a deadlock if
            // tesseract writes enough output to fill an unread pipe buffer.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = new CancellationTokenSource(PerImageTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                TryKill(process);
                throw new InvalidOperationException("OCR timed out processing an image");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                    ? $"tesseract exited with code {process.ExitCode}"
                    : stderr.Trim());
            }

            return stdout;
        }
        finally
        {
            try { File.Delete(tempFile); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
    }

    private static string ExtensionFor(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg",
    };
}
