# Short URL API

A .NET 8 minimal API prototype for creating compact links, redirecting visitors, and reading click statistics. The prototype is runnable locally and in Docker; see [Architecture](docs/architecture.md) and [Engineering workflow](docs/engineering-workflow.md) for decisions, scenarios, validation, and remaining production work.

## Requirements

- .NET 8 SDK or later
- Docker Engine and Docker Compose for the container workflow

## Run locally

```powershell
dotnet restore
dotnet run --project ShortUrl.Api.csproj --urls http://localhost:5000
```

Development uses SQLite (`shorturl.db`) and the development-only key in `appsettings.Development.json`. That key is public and must never be reused outside a local development environment. The live process exposes Swagger at `http://localhost:5000/swagger`.

## API

| Method | Path | Authentication | Behavior |
| --- | --- | --- | --- |
| `POST` | `/api/links` | `X-API-Key` | Create a link; returns `201` and a `Location` header |
| `GET` | `/{code}` | Public | Redirects with `302`; returns `404` if missing or `410` if expired |
| `GET` | `/api/links/{code}/stats` | `X-API-Key` | Read total clicks and last access |
| `GET` | `/health/live` | Public | Process liveness |
| `GET` | `/health/ready` | Public | Database connectivity |

Create request example:

```http
POST http://localhost:5000/api/links
X-API-Key: dev-local-api-key-do-not-use-in-production
Content-Type: application/json

{
  "url": "https://example.com/article",
  "customAlias": "article1"
}
```

The generated code is six Base62 characters when no alias is supplied. Custom aliases are 4-32 ASCII letters, digits, or hyphens. Expiration must be in the future. Only absolute HTTP and HTTPS destinations are accepted. `ShortUrl.Api.http` contains runnable request examples.

## Run tests

```powershell
dotnet test tests/ShortUrl.Api.Tests/ShortUrl.Api.Tests.csproj
```

The suite includes service and middleware unit tests plus a `WebApplicationFactory` HTTP flow using an isolated in-memory SQLite database.

GitHub Actions runs formatting checks, a NuGet advisory gate, all tests, Release publish, and a Docker image build on pushes and pull requests.

## Run with Docker Compose

Copy `.env.example` to `.env` and replace `SHORTURL_API_KEY` with a unique random secret of at least 32 characters. Set `APP_BASE_URL` to the public URL users should receive, then run:

```powershell
Copy-Item .env.example .env
# Edit .env and replace the placeholder key.
docker compose up --build
```

Compose runs as a non-root user and stores SQLite data in a named volume mounted at `/data`. Do not commit `.env`. Production should inject secrets through a secret manager or deployment platform, not a checked-in file. To use PostgreSQL, supply a PostgreSQL connection string through `ConnectionStrings__DefaultConnection`; validate migrations and deployment behavior before production use.

## Current boundaries

This is an interview prototype, not a production approval. SQLite is suitable for local/single-instance use, and the app currently bootstraps schema with `EnsureCreated`; use reviewed EF migrations before a production rollout. See the engineering workflow document for security, scaling, analytics, and validation limitations.
