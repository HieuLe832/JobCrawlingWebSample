# CLAUDE.md

IT job crawler: crawl IT jobs from 4 Vietnamese job boards (only where robots.txt allows), then classify them.
Scope is deliberately small — two features only: **crawl** and **classify**. Prototype / feasibility check, not production.

## Stack

- Backend: C# ASP.NET Core 8 Web API — `backend/JobCrawler.Api` (AngleSharp for HTML, EF Core + SQLite `jobs.db`)
- Frontend: React 18 + Vite — `frontend/` (dev server proxies `/api` to the backend)

## Commands

```bash
# backend (http://localhost:5111, Swagger at /swagger)
cd backend/JobCrawler.Api && dotnet run --urls http://localhost:5111

# frontend (http://localhost:5173)
cd frontend && npm install && npm run dev

# crawl via API without the UI
curl -X POST localhost:5111/api/crawl -H 'Content-Type: application/json' -d '{"maxPages":2}'
curl localhost:5111/api/crawl/status
curl 'localhost:5111/api/jobs/stats'
curl -X POST localhost:5111/api/jobs/reclassify   # re-run classifier after changing rules
```

No migrations: the DB is created with `EnsureCreated()`. After changing the `JobPosting` model, delete `jobs.db`.

## Layout

| Path | What |
|---|---|
| `Crawling/PoliteHttpClient.cs` | The only HTTP entry point: robots.txt check, per-host delay, retry |
| `Crawling/RobotsTxt.cs` | RFC 9309 matcher (group merge, `*`/`$`, longest match, Allow wins ties) |
| `Crawling/Sources/*.cs` | One `IJobSource` per site |
| `Crawling/CrawlCoordinator.cs` | Runs a crawl in the background, upserts by (Source, ExternalId), classifies |
| `Classification/JobClassifier.cs` | Rule-based classifier: IT / Non-IT, category, level |
| `robots.txt_here/` | Snapshot of each site's robots.txt (2026-10-15), for reference only — the crawler fetches live |

## Sources (how each one is crawled and why)

| Site | Method | Notes |
|---|---|---|
| ITViec | HTML `/it-jobs?page=N`, `div.job-card` | 20/page. All jobs are IT. Salary needs login → empty |
| TopCV | HTML `/tim-viec-lam-cong-nghe-thong-tin-cr257?page=N` | ~50/page. Category also contains sales / design jobs |
| TopDev | `sitemap-jobs.xml` → `jobs_desc_vi_page_N.xml` → job page JSON-LD `JobPosting` | Search results load from `api.topdev.vn`, whose robots.txt is `Disallow: /` — **do not call that API** |
| VietnamWorks | POST `ms.vietnamworks.com/job-search/v1.0/search`, filter jobFunction parentId 5 (IT) | Listing HTML has only 5 SSR jobs. `ms.` host has no robots.txt (404 = allowed) |

## Conventions

- Every network call goes through `PoliteHttpClient`. Never use `HttpClient` directly in a source.
- Default politeness: 1500 ms between requests to the same host, sequential per host, honest bot User-Agent
  (`JobCrawlerBot`, configurable in `appsettings.json` → `Crawler`).
- A new source = a class implementing `IJobSource` + one `AddSingleton<IJobSource, ...>` line in `Program.cs`
  + its key in `frontend/src/sources.js`.
- Comments explain *why* (site quirks, false positives), not what.
- Before adding a site, read its robots.txt for the exact paths/hosts you will hit, including API subdomains.

## Known pitfalls

Crawling
- **API subdomains have their own robots.txt.** `topdev.vn` allows crawling but `api.topdev.vn` disallows everything.
  Check the robots.txt of every host you call, not just the main site.
- **robots.txt status codes (RFC 9309):** 4xx → no restrictions; 5xx / network error → treat as disallow-all.
- **Repeated groups:** ITViec lists `User-Agent: *` twice — rules of all groups for the same agent must be merged.
- **Next.js sites hide data.** TopDev uses React Server Components (`self.__next_f.push`), VietnamWorks client-side fetch.
  The HTML "listing" may only contain promoted jobs that look like results. Compare page 1 vs page 3 before trusting it.
- **JSON-LD is the most stable source** on detail pages, but not every field is truthful: TopDev's `industry` is always
  "Information Technology", even for construction jobs.
- **Sitemaps contain expired jobs** (TopDev ~30% 404). 404/410 is returned as `null`, not an error.
- **Redirects:** old TopCV category URLs 301 to new ones; robots is checked on the requested URL only, so use canonical URLs.
- **Selectors break when sites redesign.** Each source yields nothing (not an exception) when its selector finds no cards —
  if a source suddenly returns 0 jobs, re-check the HTML first.
- Don't scrape behind login (salary on ITViec/TopDev) and don't touch disallowed paths (`/sign_in`, `/job/getApply`, ...).

Classification
- **Vietnamese without diacritics collides with English acronyms:** `ít`→`it`, `ai` (who)→`ai`, `sắp`→`sap`,
  `nhưng` (but)→`nhung` (embedded). Acronyms are matched case-sensitively on the original text, and only in
  title / skills / site category, never in descriptions.
- **Word boundaries:** `java` must not hit `javascript`; `.net`, `c#`, `c++` contain non-word characters — use the
  `(?<![\p{L}\p{N}])...(?![\p{L}\p{N}])` pattern, not `\b`.
- **Overlapping phrases:** match longest keywords first and blank them out (`react native` ≠ Frontend).
- **Tech stack in the title** ("QA Engineer (Python, JS)") must not beat the role word — roles weigh 6, techs 3.
- **"IT category" ≠ IT job:** sales, marketing, graphic design, HR, CNC operators, bank tellers appear under IT on
  TopCV / TopDev / VietnamWorks (~15% of TopCV, ~35% of TopDev in a 2026-10-15 sample).
- **Non-IT title words need context:** a title starting "Tuyển dụng ..." means "Hiring ...", not an HR job;
  "kiến trúc sư hệ thống / giải pháp" is a system / solution architect, not a building architect.
- "engineer" alone is not proof of IT (process / civil / sales engineer); "developer" / "lập trình viên" is.
- "Product/Project Manager" is a role, not a Manager seniority level.
- After changing rules, call `POST /api/jobs/reclassify` instead of re-crawling.
