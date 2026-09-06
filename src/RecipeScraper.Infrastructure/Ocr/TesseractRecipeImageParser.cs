using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RecipeScraper.Core;
using RecipeScraper.Core.Abstractions;

namespace RecipeScraper.Infrastructure.Ocr;

/// <summary>Extracts a <see cref="Recipe"/> out of photographed/scanned recipe-page images by shelling out
/// to the <c>tesseract</c> CLI for OCR (no cloud API, no per-request cost) and running the result through
/// <see cref="OcrRecipeTextParser"/>. Images are processed sequentially to keep CPU/memory bounded — this
/// is a low-traffic, personal-scale endpoint, not a batch pipeline.</summary>
public sealed class TesseractRecipeImageParser(
    OcrRecipeTextParser textParser, string tesseractExecutable, ILogger<TesseractRecipeImageParser> logger)
    : IRecipeImageParser
{
    // Allows for slow or cold-started free-tier hosting while bounding per-image processing time.
    private static readonly TimeSpan PerImageTimeout = TimeSpan.FromSeconds(60);

    public async Task<Recipe> ParseAsync(IReadOnlyList<RecipeImage> images, CancellationToken cancellationToken)
    {
        var pages = new List<string>(images.Count);
        for (var i = 0; i < images.Count; i++)
        {
            pages.Add(await RunOcrAsync(images[i], i, cancellationToken));
        }

        return textParser.Parse(string.Join("\n\n", pages));
    }

    /// <summary>Runs <c>tesseract --version</c> once, so a boot-time caller can log whether OCR is even
    /// functional in the current environment before any real request depends on it.</summary>
    public async Task<string> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = tesseractExecutable,
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        await process.WaitForExitAsync(linkedCts.Token);

        // `tesseract --version` writes its banner to stderr, not stdout.
        var output = (await stdoutTask) + (await stderrTask);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"tesseract --version exited with code {process.ExitCode}: {output}");
        }
        return output.Split('\n')[0].Trim();
    }

    private async Task<string> RunOcrAsync(RecipeImage image, int imageIndex, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{ExtensionFor(image.MediaType)}");
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "OCR starting for image {ImageIndex} ({Bytes} bytes, {MediaType}) via \"{Executable}\", temp file {TempFile}",
            imageIndex, image.Data.Length, image.MediaType, tesseractExecutable, tempFile);
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
            logger.LogInformation("OCR process started for image {ImageIndex}, pid {Pid}", imageIndex, process.Id);

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
                logger.LogWarning(
                    "OCR timed out for image {ImageIndex} after {ElapsedMs}ms (pid {Pid})",
                    imageIndex, stopwatch.ElapsedMilliseconds, process.Id);
                throw new InvalidOperationException("OCR timed out processing an image");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            logger.LogInformation(
                "OCR process for image {ImageIndex} exited with code {ExitCode} after {ElapsedMs}ms",
                imageIndex, process.ExitCode, stopwatch.ElapsedMilliseconds);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                    ? $"tesseract exited with code {process.ExitCode}"
                    : stderr.Trim());
            }

            return stdout;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "Failed to run OCR for image {ImageIndex} after {ElapsedMs}ms",
                imageIndex, stopwatch.ElapsedMilliseconds);
            throw;
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
