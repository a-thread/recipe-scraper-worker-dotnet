# Recipe Scraper API (.NET)

An ASP.NET Core Minimal API port of [`recipe-scraper-worker`](https://github.com/) (the Cloudflare Workers/TypeScript
version): given `GET /?url=<recipe-page>`, it fetches that page, parses it with [AngleSharp](https://anglesharp.github.io/)
into a `Recipe`, and returns JSON. The parsing heuristics (selectors, ingredient grouping, step-badge stripping,
fraction normalization) are a line-for-line port of the TypeScript/Cheerio version, so both APIs return identical
shapes for the same input.

It also exposes `POST /import/images` for recipes that only exist on paper or in photos (e.g. a family cookbook):
given one or more images of a recipe's page(s), it uses self-hosted OCR (Tesseract) and heuristic text parsing
to extract the same `Recipe` shape, so the frontend can treat URL, HTML, and image import interchangeably. See
**Image import** below.

Structured as three layers, dependencies pointing inward:

```
RecipeScraper.Presentation  --->  RecipeScraper.Infrastructure  --->  RecipeScraper.Core
        \_____________________________________________________________/
```

- **`RecipeScraper.Core`** — the innermost layer, with no dependency on ASP.NET Core, AngleSharp, or anything
  else external. Holds the `Recipe`/`StepIngredient` entities, the `IRecipeParser`/`IRecipeHtmlFetcher`/`IRecipeCache`
  ports (interfaces), the `SsrfGuard` input-validation policy, and the `ScrapeRecipeUseCase` that orchestrates them.
- **`RecipeScraper.Infrastructure`** — implements Core's ports: `AngleSharpRecipeParser`, `HttpRecipeHtmlFetcher`
  (via `IHttpClientFactory`, wrapped in a standard resilience handler — retry + circuit breaker + timeout), and
  `MemoryRecipeCache` (via `IMemoryCache`).
- **`RecipeScraper.Presentation`** (`src/RecipeScraper.Presentation`) — the composition root. Wires up DI, exposes
  the Minimal API endpoint, CORS, health checks, and Swagger, maps `ScrapeRecipeResult` to HTTP status codes, and
  translates the `Recipe` entity to a snake_case `RecipeResponse` DTO (`Contracts/`) — the JSON wire format is a
  presentation concern, so it's kept out of Core.
- **`test/RecipeScraper.Tests`** — xUnit tests against `Core` (the use case, with fake ports) and `Infrastructure`
  (the parser and SSRF guard) directly, no HTTP involved.

## Running locally

```
dotnet run --project src/RecipeScraper.Presentation
```

This opens **Swagger UI** in your browser automatically (`launchSettings.json` points the default profile at
`/swagger`). Or manually:

```
curl -G http://localhost:5004/ --data-urlencode "url=https://example.com/some-recipe"
```

> Swagger UI is enabled unconditionally (not gated to `Development`) since this is a portfolio/demo API meant to be
> browsable wherever it's deployed. A production API handling non-public data would typically restrict it.

## Image import

`POST /import/images` accepts `multipart/form-data` with one or more files under the `images` field (up to
6 images, 10MB each — `image/jpeg`, `image/png`, or `image/webp`) and returns the same `RecipeResponse` shape
as the URL/HTML import paths. Under the hood, `TesseractRecipeImageParser`
(`RecipeScraper.Infrastructure/Ocr/TesseractRecipeImageParser.cs`) shells out to the `tesseract` CLI per
image (no cloud API, no per-request $ cost), concatenates the recognized text across all submitted images
in order, and runs it through `OcrRecipeTextParser` — a regex/heuristic text parser (title, ingredients,
steps, prep/cook time, servings) in the same spirit as `AngleSharpRecipeParser`, just operating on OCR'd
plain text instead of HTML. Because the frontend always treats the result as a pre-filled draft the user
reviews before saving, this doesn't need to be perfect — a reasonable first pass the user can hand-correct
is the bar.

Requires `tesseract` to be installed and resolvable, either on `PATH` (the default the Dockerfile sets up
via `apt-get install tesseract-ocr tesseract-ocr-eng`) or at a path given via `Ocr:TesseractExecutable` in
configuration (useful for local `dotnet run` on a machine where it isn't already on `PATH` — install it via
your OS package manager, e.g. `apt install tesseract-ocr`, `brew install tesseract`, or the
[Windows installer](https://github.com/UB-Mannheim/tesseract/wiki)).

```bash
curl -X POST http://localhost:5004/import/images \
  -F "images=@page1.jpg" -F "images=@page2.jpg"
```

## Testing

```
dotnet test
```

## Deployment

A `Dockerfile` is included for container-based hosting (Azure Container Apps, Fly.io, Render, etc.):

```
docker build -t recipe-scraper-api .
docker run -p 8080:8080 recipe-scraper-api
```

Currently deployed on [Render](https://render.com)'s free tier, auto-deploying from `main` via the Dockerfile.

## Best-practices inventory

Things deliberately in place, and why:

- **Layering with a one-way dependency rule** — `Core` has zero framework references, so the parsing/orchestration
  logic is trivially unit-testable and swappable (e.g. drop in a Redis-backed `IRecipeCache` without touching `Core`
  or `Presentation`).
- **Result types instead of exceptions for expected failures** — `ScrapeRecipeResult.Success/InvalidUrl/FetchFailed`
  makes validation/fetch failures part of the use case's return type, not exception-driven control flow; exceptions
  are reserved for genuinely unexpected faults.
- **`Directory.Build.props`** at the repo root centralizes `TargetFramework`/`Nullable`/`ImplicitUsings` instead of
  repeating them in every `.csproj`.
- **CORS via the built-in middleware** (`AddCors`/`UseCors`) rather than hand-setting the
  `Access-Control-Allow-Origin` header — correct for all request types (including preflight), not just simple GETs.
- **Resilient outbound HTTP** — `AddStandardResilienceHandler()` on the recipe-source `HttpClient` gives retry with
  backoff, a per-attempt timeout, an overall request timeout, and a circuit breaker for a dependency (third-party
  recipe sites) that's expected to be flaky.
- **`/healthz`** via `AddHealthChecks()`/`MapHealthChecks()` — a cheap liveness endpoint any container platform
  (Render, Azure Container Apps, k8s) can probe.
- **CI builds the Docker image, not just the .NET solution** — `dotnet build` succeeding doesn't guarantee the
  Dockerfile still matches the project layout (a stale path/filename reference in it will still build fine locally
  while the container build breaks); the workflow runs `docker build` too so that class of drift fails in CI.
- **Domain model kept free of serialization attributes** — `Recipe`/`StepIngredient` in `Core` have no
  `[JsonPropertyName]`; the snake_case wire format is defined once, in `Presentation/Contracts/RecipeResponse.cs`,
  so `Core` doesn't know or care that its output happens to be JSON today.

Known, deliberately-not-fixed-here gaps (would matter more in a real production service than in a portfolio piece):

- **Cache is per-instance** (`IMemoryCache`), not shared across replicas/regions — fine for a single free-tier
  instance; a multi-instance deployment would want a Redis-backed `IRecipeCache` instead (the interface already
  supports swapping this in without touching `Core` or `Presentation`).
- **No auth/rate-limiting** on the scrape endpoint — acceptable for a demo; a public production deployment would
  want at least basic rate limiting given it makes outbound requests on the caller's behalf.
- **`/import/images` has no auth/rate-limiting either** — OCR is CPU-bound and self-hosted (no per-request $
  cost like a cloud API would add), but the per-request size cap (6 images/10MB each) plus a 20s per-image
  timeout still exist to bound worst-case CPU time per call on a free-tier container, since the wide-open
  CORS policy means anyone who finds the URL can call it.
- **OCR/heuristic parsing is a first-pass extraction, not a guarantee** — no image preprocessing
  (deskew/binarization beyond what Tesseract does internally), ingredient sub-group detection is a simple
  colon-suffix heuristic, and WebP support depends on the Tesseract/Leptonica build the base image ships
  with (an untested WebP submission that fails just surfaces as an ordinary `ParseFailed` 502, not a crash).
  Acceptable given the frontend always shows the result as an editable draft before saving.
- **Meta-tag description extraction**: the shared parsing logic (both TypeScript versions) reads `<meta>` element
  text via `.text()`/`TextContent`, which is always empty for `<meta>` tags (their value lives in the `content`
  attribute) — a latent bug in the original logic. This port reads `content` for `<meta>` elements specifically, so
  `og:description`/`twitter:description` fallbacks actually work here. Worth backporting to the TypeScript versions.
