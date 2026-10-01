# Architecture

## Components

- **Minimal API (`Program.cs`)** maps link creation, redirect resolution, statistics, Swagger, and liveness/readiness routes.
- **API-key middleware** protects the management route family. Redirects and health probes remain public.
- **`ILinkService` / `LinkService`** own URL and alias validation, code allocation, expiration, click accounting, and response mapping.
- **`ICodeGenerator` / `Base62CodeGenerator`** provide injectable cryptographically random short codes.
- **EF Core / `ApplicationDbContext`** persist links with unique indexes on code and custom alias. SQLite is the local/container default; PostgreSQL is selected for a non-SQLite connection string.
- **xUnit** tests service rules, middleware behavior, and the HTTP flow through the real application pipeline.

```mermaid
flowchart LR
    Client[API client or browser] -->|POST /api/links, API key| Middleware[API-key middleware]
    Middleware --> Endpoints[Minimal API endpoints]
    Client -->|GET /{code}| Endpoints
    Client -->|GET stats, API key| Middleware
    Endpoints --> Service[LinkService]
    Service --> Generator[Base62CodeGenerator]
    Service --> EF[EF Core]
    EF --> DB[(SQLite or PostgreSQL)]
    Endpoints --> Swagger[Swagger / OpenAPI]
    Probe[Orchestrator] --> Health[Health endpoints]
    Health --> DB
```

## Request flows

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant Auth as API-key middleware
    participant Service as LinkService
    participant DB as EF Core database
    Client->>API: POST /api/links + X-API-Key
    API->>Auth: Validate management request
    Auth->>Service: Validate URL/alias and create
    Service->>DB: Check code, insert under unique index
    API-->>Client: 201 Created + shortUrl + Location
    Client->>API: GET /{code}
    API->>Service: Resolve code
    Service->>DB: Read destination; atomically increment clicks
    API-->>Client: 302 Found (or 404/410)
    Client->>API: GET /api/links/{code}/stats + X-API-Key
    API->>Auth: Validate management request
    Auth->>Service: Read statistics
    Service->>DB: Read aggregate fields
    API-->>Client: 200 OK
```

## Key decisions

- Six-character Base62 codes retain compact TinyURL-style links. There are $62^6$ possible codes (about 56.8 billion); the unique database index is authoritative, and allocation retries up to five times. This is not an unlimited namespace: monitor collision/retry rates and increase code length before operating at high volume.
- A database-side increment (`TotalClicks = TotalClicks + 1`) avoids lost click updates from concurrent redirects. A click means an unexpired short-code resolution that is issued as a redirect; the server cannot know whether the browser completed the destination request. Prefetchers and bots can count.
- Redirects are public. Link creation and statistics use a shared API key. Swagger describes the key only on management operations.
- The redirect response is temporary (`302`) and explicitly not cached. Permanent redirects could make link edits/expiration difficult to enforce in clients and intermediary caches.
- The configured `App:BaseUrl` controls generated links. Set it to the externally visible origin behind a reverse proxy; do not trust arbitrary request `Host` values in production.
- SQLite enables a simple local and single-instance deployment. It is not the horizontal-scaling choice. PostgreSQL or another shared database, migrations, and multi-instance concurrency validation are required before scaling out.
- The current prototype uses `EnsureCreated` for convenience. Production schema changes need reviewed migrations and a controlled deployment step.

## Data and API contract

A link stores its opaque code, destination URL, creation/expiration timestamps, optional custom alias, total click count, and last access time. Unique indexes prevent duplicate codes and aliases. Statistics currently expose only aggregate click count and last access; no IP address, user agent, referrer, or per-day event stream is collected.

OpenAPI is generated at `/swagger/v1/swagger.json`. API-key security is attached to the management route operations; the short-code redirect and health operations do not require it. See the API table and examples in the repository README.
