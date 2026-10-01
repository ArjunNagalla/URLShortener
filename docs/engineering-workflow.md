# Engineering Workflow and Traceability

## Requirement normalization

The assignment asks for an engineer-led system that turns requirements into reviewable work. For this prototype, the product boundary is a single-tenant URL shortener with a public redirect endpoint and API-key-protected management endpoints. The service accepts only absolute HTTP(S) destinations, supports optional custom aliases and expiration, and returns aggregate click statistics.

Assumptions made explicit:

- “Click” means the server successfully resolved a non-expired code and issued a redirect. It does not prove that a browser loaded the destination.
- Expiration is evaluated by the API in UTC. An expired link returns `410 Gone` and does not increment its click count.
- Codes and aliases are case-sensitive. Six-character Base62 is the default; custom aliases are 4-32 ASCII letters, digits, or hyphens.
- Redirects are public; creation and statistics require one configured API key. No tenant model, user identity, rate limiting, or alias ownership is specified.
- No IP address, referrer, or user-agent collection is inferred from “analytics”; only aggregate count and last access are stored.

## Decomposition and sequencing

| ID | Intent and technical context | Acceptance criteria | Depends on |
| --- | --- | --- | --- |
| REQ-01 | Normalize URL, alias, expiration, and analytics semantics | Assumptions are recorded; invalid schemes, expired timestamps, and invalid aliases have deterministic outcomes | None |
| DATA-01 | Model links and persistence with EF Core | Unique indexes enforce code/alias uniqueness; local database can initialize | REQ-01 |
| SVC-01 | Isolate link lifecycle rules behind `ILinkService` | Create, resolve, expire, and stats behavior can be tested without HTTP | DATA-01 |
| API-01 | Expose create, redirect, stats, health, and OpenAPI endpoints | Correct status codes and `Location`; only management endpoints require the API key | SVC-01 |
| REL-01 | Bound collision handling and protect analytics under concurrency | Allocation retries are bounded and include the final attempt; click updates are atomic | DATA-01, SVC-01 |
| SEC-01 | Prevent management-route auth bypass and protect deployment secrets | Route case variants remain protected; production refuses missing/weak keys; image is non-root | API-01 |
| TEST-01 | Verify service, middleware, and end-to-end behavior | Unit and HTTP integration tests cover the acceptance criteria | API-01, REL-01, SEC-01 |
| DOC-01 | Make operation and AI-assisted execution reviewable | Setup, architecture, scenarios, traceability, risks, and limitations are published | All prior tasks |

## Scenarios

### Greenfield: create the shortener

**Decomposition:** establish the `Link` entity and unique indexes; define request/response records and an injectable code generator; implement create/resolve/stats behavior in `ILinkService`; map HTTP routes; add unit and HTTP tests; document local and container setup.

**Execution:** `Program.cs` hosts minimal endpoints, `LinkService` owns business rules, `ApplicationDbContext` persists links, and `Base62CodeGenerator` creates compact codes. Dependency injection keeps the generator and service replaceable in tests. SQLite is the local default; PostgreSQL remains selectable by connection string.

**Validation:** service tests exercise link creation, final-attempt code allocation, and click counts. The integration test sends HTTP requests through the real app pipeline and checks key enforcement, `201 Created`, `302 Found`, and statistics. Liveness/readiness endpoints are available for runtime probes.

### Brownfield: harden an existing management API

**Decomposition:** trace route dispatch through middleware and endpoint routing; form a testable hypothesis about path matching; add a regression test before broad refactoring; check Swagger’s operation-level security; verify deployment secret flow.

**Execution:** case-sensitive string-prefix logic could let route-casing variants reach case-insensitive ASP.NET routes without passing auth. Middleware now recognizes the management path case-insensitively with a segment boundary. Swagger marks only management operations as API-key protected. Key bytes are compared with `CryptographicOperations.FixedTimeEquals`. Production startup rejects missing or shorter-than-32-character API keys.

**Validation:** unit tests cover lowercase and uppercase create/stats paths with missing keys, plus the public redirect path. The integration test checks unauthenticated rejection and the authenticated create/redirect/stats sequence. The API and tests are built to a separate output directory during development because the local running process locks the normal output.

### Ambiguous: define what analytics counts

**Ambiguity:** a request to a short URL may come from a browser, crawler, link preview, prefetch, retry, or a client that never completes the destination request. The assignment does not define deduplication or privacy scope.

**Decision:** count each successful, unexpired code resolution when the service issues a redirect. Keep only a total and last-access timestamp; do not collect client identifiers. An expired resolution returns `410` and does not count.

**Execution:** the resolution service checks the stored expiration and updates click count in SQL as `TotalClicks + 1`, rather than loading and saving a stale counter value.

**Validation:** service tests verify repeated resolutions increase the total; the HTTP integration test confirms one redirect is reflected in stats. Browser completion, bot exclusion, deduplication windows, and per-day analytics remain out of scope and require product/privacy decisions.

## AI-assisted execution record

AI is an implementation assistant, not an approver or deployment actor. The engineer provides scoped tasks, evaluates the output, runs quality gates, and owns correctness and production readiness. Current code and documents are AI-assisted and remain subject to human review/sign-off.

| Work item | AI-assisted output | Engineer-led disposition and evidence | Sign-off |
| --- | --- | --- | --- |
| SEC-01 | Middleware path matching and regression tests | Kept after tests reproduced the case-variant behavior and passed after the fix | Pending |
| REL-01 | Bounded collision retry and atomic click-update implementation | Kept after focused SQLite tests passed, including a unique code on attempt five and repeated click totals | Pending |
| TEST-01 | Unit tests and an HTTP integration flow | Kept after the retained project passed 14 tests; test fixture uses an isolated in-memory SQLite connection | Pending |
| BUILD-01 | Initial test scaffolding exposed API project compiling nested test source files | Fixed at the project boundary by excluding `tests/**/*.cs` from the API compile glob; consolidated duplicate test project | Pending |
| DOC-01 | README, architecture, scenario, and review workflow drafts | Added as reviewable project artifacts; factual claims are tied to current implementation and test results | Pending |
| SEC-02 | Proposal to keep a shared API key in application/container defaults | Rejected: production keys must be injected; Compose now requires an external value and development uses a clearly non-production key | Implemented; pending review |
| API-02 | Global Swagger API-key requirement | Rejected: it incorrectly describes public redirects as protected; requirement is now attached to management operations | Implemented; pending review |
| PRIV-01 | Add IP/referrer analytics without a product requirement | Rejected: privacy, retention, and deduplication semantics are undefined | Deferred |

### Prompt discipline and secure use

For each AI task, provide: **intent**, **relevant files/data flow**, **constraints**, **acceptance criteria**, and **required validation**. Example:

> Intent: prevent API-key bypass on management routes. Context: `ApiKeyAuthMiddleware` runs before minimal API endpoints; endpoint routing is case-insensitive. Constraints: redirects stay public; do not change route names or key header. Acceptance: casing variants of create/stats return 401 without a key, while `/{code}` reaches the next middleware. Add focused tests and report exact commands/results. Do not add secrets or deploy.

Never include production API keys, customer URLs, personal data, or database contents in prompts. Use synthetic test data and environment-injected secrets. AI-generated code is not merged or deployed automatically. The human reviewer must inspect the diff, verify behavior and threat assumptions, approve dependency/license changes, and explicitly sign off high-impact database, auth, and deployment changes.

## Validation gates and evidence

| Gate | Evidence in this prototype | Remaining limitation |
| --- | --- | --- |
| Build | API and test projects compile; Release publish succeeds; GitHub Actions builds the container image | Container image build has not been run locally because Docker is unavailable |
| Unit tests | Generator/service/middleware tests in xUnit | No load or stress benchmark |
| Integration tests | In-process HTTP flow covers create, redirect, auth, and stats | SQLite only; PostgreSQL deployment path is not exercised |
| API contract | Swagger/OpenAPI metadata, response statuses, and `.http` examples | No generated OpenAPI compatibility diff in CI |
| Security review | Case-insensitive auth regression, production key validation, fixed-time comparison, non-root container, CI NuGet audit as an error gate | No rate limiting, rotation workflow, identity/tenant access control, or automated secret scanner |
| Performance/reliability | Indexed code lookup, bounded collision attempts, SQL-side atomic counters | Six-character namespace, SQLite write throughput, and high-contention behavior need measured load tests |

## Risks, trade-offs, and follow-up

| Risk | Current control | Before production |
| --- | --- | --- |
| Six-character namespace collision pressure rises with volume | Unique database constraint and five bounded retries | Measure collision rates and lengthen codes before capacity is approached |
| Shared API key can be copied or abused | Production key is externally configured and minimum length enforced | Add secret rotation, rate limiting, identity/authorization, audit logging, and key revocation |
| SQLite is a single-instance persistence choice | Named Docker volume preserves data | Use PostgreSQL or another shared database for multiple instances; add and review migrations |
| `EnsureCreated` does not evolve existing schemas | Convenient empty-database bootstrap for this prototype | Replace with versioned EF migrations and a controlled migration job |
| The application targets .NET 8, whose support ends November 10, 2026 | Current runtime and package lines are pinned to .NET 8 | Plan and validate a move to .NET 10 before the .NET 8 support deadline |
| Redirect analytics can count bots/prefetch and cannot prove landing-page completion | Metric is explicitly named a resolution/redirect count | Confirm product definition; add deduplication only with a privacy-reviewed policy |
| Arbitrary external destinations can be abused for phishing | Only HTTP/HTTPS URLs accepted; server never fetches the destination | Add abuse reporting, reputation controls, monitoring, and takedown policy |
| Reverse-proxy URL/scheme may be wrong if misconfigured | `App:BaseUrl` is configurable | Set trusted forwarded-header configuration and validate externally generated URLs |

## Human review checklist

- [ ] Confirm assumptions for click semantics, expiration, aliases, and six-character codes.
- [ ] Review the auth middleware and production API-key startup requirement.
- [ ] Review database provider behavior and decide whether SQLite is acceptable.
- [ ] Inspect all AI-assisted diffs and dependency changes; approve or request changes.
- [ ] Run the documented build/test/security checks in the target environment.
- [ ] Approve migrations, secret provisioning, monitoring, rate limits, and rollout/rollback before production.
