# LiteGate — lightweight rate-limiting API gateway

A small reverse proxy built on **pure ASP.NET Core** (no YARP) that:

- forwards incoming HTTP requests to a configurable upstream,
- caches identical `GET` responses in memory (`IMemoryCache`),
- limits request frequency per client IP with a classic **token bucket**,
- logs stage-by-stage latency and status codes as **structured JSON**,
- exposes `/_health` and `/_metrics` endpoints.

## Architecture

```
                 ┌────────────────────────────────────────────────────────────┐
                 │                          LiteGate                          │
                 │                                                            │
  Request ──────▶│ RequestLogging ─▶ RateLimiting ─▶ ResponseCache ─▶ Proxy ──┼──────▶ Upstream
                 │   (trace id,      (token bucket   (IMemoryCache,   (stream │        :5100
                 │    stage timer)    per IP, 429)    X-Cache hdr)    copy)   │
                 │                                                            │
                 │   /_health        /_metrics (status codes, latency p50/95/99)
                 └────────────────────────────────────────────────────────────┘
```

The middleware pipeline runs in the order shown. Reserved paths (`/_health`, `/_metrics`)
and any request matched by a local endpoint bypass rate limiting, caching and proxying.

## Quick start

```bash
dotnet build LiteGate.slnx
bash scripts/demo.sh       # starts upstream :5100 + gateway :5080 and exercises everything
```

Or run the two services manually and try it yourself:

```bash
dotnet run --project src/LiteGate.DemoUpstream --urls http://localhost:5100
dotnet run --project src/LiteGate.Gateway     --urls http://localhost:5080
```

```bash
curl -i http://localhost:5080/api/time    # X-Cache: MISS — first call reaches the upstream
curl -i http://localhost:5080/api/time    # X-Cache: HIT  — served from memory
curl -i http://localhost:5080/_metrics    # counters + latency percentiles
```

## Configuration (`appsettings.json`, section `Gateway`)

| Option | Default | Meaning |
| --- | --- | --- |
| `ReservedPaths` | `["/_health", "/_metrics"]` | Paths served by the gateway itself |
| `Upstream:BaseAddress` | `http://localhost:5100` | Target the proxy forwards to |
| `Upstream:TimeoutSeconds` | `30` | Upstream call timeout (results in `504`) |
| `Cache:Enabled` | `true` | Toggle the response cache |
| `Cache:DurationSeconds` | `30` | Fallback TTL when upstream sends no `Cache-Control` |
| `Cache:MaxCacheableBodyBytes` | 1 MiB | Larger bodies stream through uncached |
| `Cache:MaxStoreBytes` | 50 MiB | Total in-memory cache budget |
| `Cache:VaryByHeaders` | `[]` | Extra request headers that partition the cache key |
| `RateLimiting:Capacity` | `10` | Token bucket size (allowed burst) |
| `RateLimiting:RefillTokens` / `RefillPeriodSeconds` | `5` / `1` | Refill rate per bucket |
| `RateLimiting:IdleBucketEvictionMinutes` | `10` | Background sweep evicts idle buckets |
| `RateLimiting:TrustXForwardedFor` | `false` | Use the first `X-Forwarded-For` hop as the client identity |

All options have sensible defaults, so an empty `Gateway` section still works.

## Cache policy

- Only `GET` requests are cached; everything else is proxied verbatim.
- Cache key: `method | path | query` plus configured `VaryByHeaders` values.
- Upstream `Cache-Control` is honored: `no-store`, `no-cache`, `private` and `Set-Cookie`
  responses are never cached; `max-age=N` overrides the configured TTL.
- Cached responses are replayed with an `Age` header; live responses carry
  `X-Cache: MISS`, served ones `X-Cache: HIT`.
- `Accept-Encoding` is stripped from forwarded requests so bodies are stored uncompressed.

## Rate limiting algorithm

A per-client token bucket (`RateLimiting/TokenBucket.cs`): the bucket starts with
`Capacity` tokens, each request consumes one, and tokens refill continuously at
`RefillTokens / RefillPeriodSeconds` per second, capped at capacity. When the bucket
is empty the gateway replies **429** with `Retry-After` and `X-RateLimit-*` headers.
The bucket clock is an injected `TimeProvider`, which makes the algorithm unit-testable
with virtual time.

## Structured logging

All logs are emitted as JSON (console `json` formatter). Every request produces one
`Information` entry with the trace id, status code, total latency and per-stage timings:

```json
{"LogLevel":"Information","MessageTemplate":"Gateway request {TraceId} {Method} {Path} responded {StatusCode} in {TotalMs} ms ...","TraceId":"...","StatusCode":200,"TotalMs":12.4,"RateLimitMs":0.02,"CacheLookupMs":0.01,"CacheStoreMs":0.3,"UpstreamMs":11.8,...}
```

`Debug` level additionally logs each stage boundary (rate limit decision, cache hit/miss,
upstream duration) and the token-bucket state per client.

## Metrics

`GET /_metrics` returns a JSON snapshot: total/throttled/cache-hit/cache-miss/upstream-error
counters, a breakdown by status code and rolling `p50/p95/p99` latency over the last
`Observability:LatencySampleWindowSize` requests.

## Tests

```bash
dotnet test LiteGate.slnx
```

20 tests cover the token bucket (virtual time), per-client isolation and bucket eviction,
the response cache (hit/miss, query separation, `no-store`, non-GET bypass, TTL expiry)
and the proxy (header forwarding, `X-Forwarded-*`, status/body passthrough, 502 on
unreachable upstream). Integration tests boot the full gateway in memory
(`WebApplicationFactory`) against a stub upstream handler — no network involved.

## Roadmap

- Redis-backed response cache and shared rate-limit state via `IDistributedCache`
  (the `IResponseCache` abstraction is ready for it)
- Optional YARP-based forwarding for advanced routing transforms
- Distributed rate limiting across gateway instances
