using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using RecipeScraper.Presentation.Contracts;
using RecipeScraper.Core;
using RecipeScraper.Core.UseCases;
using RecipeScraper.Infrastructure;
using RecipeScraper.Infrastructure.Ocr;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRecipeScraperCore();
builder.Services.AddRecipeScraperInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    // This is a public, read-only scraping endpoint with no credentials or user-specific data involved.
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Recipe Scraper API",
        Version = "v1",
        Description = "Scrapes structured recipe data (title, ingredients, steps, timings) out of a recipe web page.",
    });
});

var app = builder.Build();

// Logs whether tesseract is actually invocable in this environment at boot time, rather than only
// discovering it's broken on the first real /import/images request — surfaces in the same application
// logs a container platform (e.g. Render) already exposes, no request/infra log access needed.
using (var startupScope = app.Services.CreateScope())
{
    var startupLogger = startupScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var tesseractParser = startupScope.ServiceProvider.GetRequiredService<TesseractRecipeImageParser>();
    try
    {
        var version = await tesseractParser.CheckAvailabilityAsync(CancellationToken.None);
        startupLogger.LogInformation("tesseract available at startup: {Version}", version);
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "tesseract is NOT available — /import/images will fail on every request");
    }
}

app.UseCors();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Recipe Scraper API v1");
    options.RoutePrefix = "swagger";
});

app.MapHealthChecks("/healthz");

app.MapGet("/", async (string? url, HttpContext context, ScrapeRecipeUseCase useCase, CancellationToken cancellationToken) =>
{
    var result = await useCase.ExecuteAsync(url, cancellationToken);

    switch (result)
    {
        case ScrapeRecipeResult.Success success:
            context.Response.Headers["Cache-Control"] = "s-maxage=86400";
            return Results.Json(RecipeResponse.FromDomain(success.Recipe));
        case ScrapeRecipeResult.InvalidUrl invalid:
            return Results.Text(invalid.Reason, statusCode: StatusCodes.Status400BadRequest);
        case ScrapeRecipeResult.FetchFailed failed:
            return Results.Text(failed.Reason, statusCode: StatusCodes.Status502BadGateway);
        default:
            throw new InvalidOperationException($"Unhandled {nameof(ScrapeRecipeResult)}: {result.GetType()}");
    }
})
.WithName("ScrapeRecipe")
.WithSummary("Scrape a recipe from a web page URL")
.WithDescription("Fetches the page at ?url=, parses it for recipe data, and returns it as JSON. Rejects non-http(s) " +
    "and internal/private-network targets.")
.Produces<RecipeResponse>(StatusCodes.Status200OK, "application/json")
.Produces<string>(StatusCodes.Status400BadRequest, "text/plain")
.Produces<string>(StatusCodes.Status502BadGateway, "text/plain");

app.MapPost("/import/images", async (
    HttpRequest request,
    ImportRecipeFromImagesUseCase useCase,
    CancellationToken cancellationToken) =>
{
    // Read the form manually rather than binding an `IFormFileCollection` parameter — that binding is
    // what makes ASP.NET Core auto-attach an antiforgery requirement (a same-origin-cookie-session
    // protection this stateless, no-auth, AllowAnyOrigin API has no use for, per the CORS comment above).
    // Reading it this way sidesteps the inference instead of disabling the protection outright.
    var form = await request.ReadFormAsync(cancellationToken);
    var images = form.Files;

    var recipeImages = new List<RecipeImage>(images.Count);
    foreach (var file in images)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        recipeImages.Add(new RecipeImage(stream.ToArray(), file.ContentType));
    }

    var result = await useCase.ExecuteAsync(recipeImages, cancellationToken);

    switch (result)
    {
        case ImportRecipeFromImagesResult.Success success:
            return Results.Json(RecipeResponse.FromDomain(success.Recipe));
        case ImportRecipeFromImagesResult.InvalidInput invalid:
            return Results.Text(invalid.Reason, statusCode: StatusCodes.Status400BadRequest);
        case ImportRecipeFromImagesResult.ParseFailed failed:
            return Results.Text(failed.Reason, statusCode: StatusCodes.Status502BadGateway);
        default:
            throw new InvalidOperationException(
                $"Unhandled {nameof(ImportRecipeFromImagesResult)}: {result.GetType()}");
    }
})
.WithName("ImportRecipeFromImages")
.WithSummary("Extract a recipe from one or more photographed/scanned recipe-page images")
.WithDescription("Accepts multipart/form-data with one or more files under the \"images\" field — photos or " +
    "scans of a recipe (e.g. cookbook pages) — and uses self-hosted OCR (Tesseract) plus heuristic text " +
    "parsing to extract structured recipe data. Useful for importing recipes that only exist on paper or " +
    $"in photos, where URL/HTML import doesn't apply. Up to {ImportRecipeFromImagesUseCase.MaxImages} " +
    $"images, {ImportRecipeFromImagesUseCase.MaxImageBytes / (1024 * 1024)}MB each.")
.WithMetadata(new RequestSizeLimitAttribute(
    ImportRecipeFromImagesUseCase.MaxImages * ImportRecipeFromImagesUseCase.MaxImageBytes))
.Produces<RecipeResponse>(StatusCodes.Status200OK, "application/json")
.Produces<string>(StatusCodes.Status400BadRequest, "text/plain")
.Produces<string>(StatusCodes.Status502BadGateway, "text/plain");

app.Run();
