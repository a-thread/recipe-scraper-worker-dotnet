# Recipe Scraper API (.NET)

An ASP.NET Core Minimal API port of [`recipe-scraper-worker`](https://github.com/) (the Cloudflare Workers/TypeScript
version): given `GET /?url=<recipe-page>`, it fetches that page, parses it with [AngleSharp](https://anglesharp.github.io/)
into a `Recipe`, and returns JSON. The parsing heuristics (selectors, ingredient grouping, step-badge stripping,
fraction normalization) are a line-for-line port of the TypeScript/Cheerio version, so both APIs return identical
shapes for the same input.

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
- **Meta-tag description extraction**: the shared parsing logic (both TypeScript versions) reads `<meta>` element
  text via `.text()`/`TextContent`, which is always empty for `<meta>` tags (their value lives in the `content`
  attribute) — a latent bug in the original logic. This port reads `content` for `<meta>` elements specifically, so
  `og:description`/`twitter:description` fallbacks actually work here. Worth backporting to the TypeScript versions.
