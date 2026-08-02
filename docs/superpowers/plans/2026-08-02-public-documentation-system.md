# Public Documentation System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build iteration 8 as a public, localized MD/MDX documentation system whose rendering stays in Next.js while ASP.NET Core owns the anonymous generated-contract search API.

**Architecture:** A deterministic Node compiler treats localized MD/MDX in `apps/web` as the authoring source and emits a typed web registry plus `contracts/documents/search-index.json`. Infrastructure embeds the neutral index, Application owns ranking, Api exposes anonymous search, and the browser calls it only through the generated SDK.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, xUnit v3, Next.js 16.2.11, React 19.2.8, TypeScript 6, next-intl 4.13.4, Tailwind CSS 4, Radix/shadcn, `@next/mdx` 16.2.11, MDX 3.1.1, Jest 30, Playwright 1.61, OpenAPI 3.1, `@hey-api/openapi-ts` 0.99.0.

## Global Constraints

- `template/` is immutable: read/search/copy/compare only; never edit, format, move, delete or run migrations inside it.
- Work only on `codex/iteration-8-public-documentation`, created from fresh `origin/main` commit `0c1e588`.
- Follow `Domain ← Application ← Infrastructure/Api`; Domain remains unchanged.
- ASP.NET Core is the only owner of `/api/**`; the sole Next Route Handler allowed is presentation-only `/docs/og/{**slug}`.
- `apps/web` has no Prisma, Better Auth, Server Actions, raw API `fetch`, handwritten transport DTOs, direct database access or browser bearer storage.
- Port 108 target-owned content files as 54 canonical `en`/`ru` routes and rewrite current guidance for ASP.NET Core + REST; historical pages label legacy behavior.
- Published/archived documents require both locales; draft/review fallback is local-authoring-only.
- Search is anonymous/no-store; success uses `{ data }`, failures use RFC Problem Details.
- `q` is trimmed and limited to 120 UTF-16 code units; locale is `en|ru`; empty query returns 32 pages, typed query at most 8 pages and 8 headings.
- Add no EF entity/migration, transaction, seed, background indexer, Redis, CMS, rate limiter, authenticated docs or locale-prefixed routes.
- Start each production behavior with an observed failing focused test, implement minimally, run focused GREEN, then commit.
- Generated content/search/OpenAPI/SDK artifacts are deterministic and never hand-edited.
- Do not create an active OpenSpec change/spec.
- Final acceptance runs every .NET, content, OpenAPI, SDK, web, build, audit, Playwright, whitespace and immutable-reference gate in the approved design.

---

## File Structure

### Compiler and generated artifacts

- `apps/web/scripts/documents-content-lib.mjs` — discovery, validation, headings, links, ordering and generation.
- `apps/web/scripts/{generate,check}-documents-content.mjs` — write and byte-compare entry points.
- `apps/web/scripts/documents-{content,corpus}.node-test.mjs` — fixture and real-corpus tests.
- `apps/web/src/features/documents/content/**` — 108 target-owned MD/MDX files.
- `apps/web/src/features/documents/generated/documents-registry.gen.ts` — generated metadata and exact import map.
- `contracts/documents/search-index.json` — generated neutral search artifact.

### Backend

- `apps/api/src/Template.Application/Documents/{DocumentSearchModels,DocumentSearchText,DocumentSearchService}.cs` and `Ports/IDocumentSearchIndexProvider.cs`.
- `apps/api/src/Template.Infrastructure/Documents/{EmbeddedDocumentSearchIndexProvider,DocumentSearchInfrastructureServiceCollectionExtensions}.cs`.
- `apps/api/src/Template.Api/Features/Documents/{DocumentSearchContracts,DocumentSearchEndpointBoundary,DocumentSearchEndpointModule}.cs`.
- `apps/api/src/Template.Api/OpenApi/{DocumentSearchContractOperationTransformer,DocumentSearchContractSchemaTransformer}.cs`.

### Web

- `apps/web/mdx-components.tsx`, documents registry/routes/navigation/heading helpers.
- `apps/web/src/components/documents/**` for shell, sidebar, article, TOC, MDX, search and copy controls.
- `apps/web/src/app/(documents)/docs/**`, `apps/web/src/app/sitemap.ts`, `apps/web/src/lib/public-origin.ts`.
- `apps/web/src/lib/api/documents/browser/search-documents.ts`, paired document message bundles and target assets.

### Tests

- Application search text/service tests; API embedded-index/endpoint/failure/OpenAPI tests.
- Web compiler/corpus, registry, content-policy, components, routes, metadata, sitemap, boundary and adapter tests.
- `apps/web/e2e/documents.spec.ts` for anonymous route/search/OG acceptance.

---

### Task 1: Deterministic Content Compiler Core

**Files:**
- Create: `apps/web/scripts/documents-content-lib.mjs`
- Create: `apps/web/scripts/generate-documents-content.mjs`
- Create: `apps/web/scripts/check-documents-content.mjs`
- Create: `apps/web/scripts/documents-content.node-test.mjs`
- Modify: `apps/web/package.json`, `apps/web/package-lock.json`

**Interfaces:**

```js
export const DOCUMENT_LOCALES = ["en", "ru"];
export async function compileDocumentsContent({ contentRoot, publicRoot }) {
  return { registrySource, searchIndexJson, documents, diagnostics };
}
export async function writeDocumentsArtifacts(options);
export async function checkDocumentsArtifacts(options);
```

- [ ] **Step 1: Install exact dependencies and scripts**

```bash
cd apps/web
npm install --save-exact @next/mdx@16.2.11 @mdx-js/loader@3.1.1 \
  @mdx-js/react@3.1.1 @types/mdx@2.0.14 remark-frontmatter@5.0.0 \
  remark-gfm@4.0.1 gray-matter@4.0.3
```

Add `content:test`, `content:generate`, and `content:check` scripts invoking the three files above.

- [ ] **Step 2: Write failing discovery/metadata/order tests**

```js
assert.equal(result.documents.length, 4);
assert.equal(result.documents[0].canonicalUrl, "index");
assert.deepEqual(result.documents[0].availableLocales, ["en", "ru"]);
await assert.rejects(runFixture("unknown-field"), /documents_metadata_unknown_field/);
await assert.rejects(runFixture("duplicate-locale"), /documents_duplicate_locale/);
await assert.rejects(runFixture("bad-date"), /documents_metadata_invalid_edited_at/);
```

- [ ] **Step 3: Run RED**

```bash
cd apps/web && npm run content:test
```

Expected: module/corpus test missing.

- [ ] **Step 4: Implement strict deterministic compilation**

Use native recursive `fs`, `gray-matter`, locale suffix removal, an exact frontmatter allow-list, ISO date validation, stable group/parent/document ordering, LF output and no clock/mtime input. Emit schema version 1:

```json
{"schemaVersion":1,"locales":{"en":{"pages":[],"headings":[]},"ru":{"pages":[],"headings":[]}}}
```

- [ ] **Step 5: Implement write/check and run GREEN**

```bash
cd apps/web
npm run content:test
npm run audit:prod
```

The check path must compare complete expected strings and fail on a missing/stale file.

- [ ] **Step 6: Commit**

```bash
git add apps/web/package.json apps/web/package-lock.json \
  apps/web/scripts/documents-content-lib.mjs \
  apps/web/scripts/generate-documents-content.mjs \
  apps/web/scripts/check-documents-content.mjs \
  apps/web/scripts/documents-content.node-test.mjs
git commit -m "feat: add deterministic documentation compiler"
```

---

### Task 2: Validation, MDX Pipeline and Reference Corpus Copy

**Files:**
- Modify: `apps/web/scripts/documents-content-lib.mjs`
- Modify: `apps/web/scripts/documents-content.node-test.mjs`
- Create: `apps/web/scripts/documents-corpus.node-test.mjs`
- Create: `apps/web/src/features/documents/content/**` (108 copied files)
- Create: generated registry and `contracts/documents/search-index.json`
- Create: `apps/web/mdx-components.tsx`
- Modify: `apps/web/next.config.ts`
- Create: `apps/web/public/img/branding/{template_logo_nb_s,web-app-manifest-512x512}.png`

**Interfaces:** compiler additionally validates headings, internal links/fragments, local images, locale publication pairs and the closed MDX set.

- [ ] **Step 1: Write failing validation/corpus tests**

```js
assert.deepEqual(extractHeadings("## Same\n## Same"), [
  { level: 2, title: "Same", id: "same" },
  { level: 2, title: "Same", id: "same-2" },
]);
await assert.rejects(runFixture("broken-link"), /documents_broken_link/);
await assert.rejects(runFixture("broken-fragment"), /documents_broken_fragment/);
await assert.rejects(runFixture("missing-image"), /documents_missing_image/);
await assert.rejects(runFixture("published-missing-ru"), /documents_missing_published_locale/);
```

Real corpus expectations are 108 variants, 54 canonical URLs, locales `en` and `ru`, zero diagnostics.

- [ ] **Step 2: Run RED**

```bash
cd apps/web && npm run content:test
```

- [ ] **Step 3: Implement validation**

Track backtick/tilde fences; validate Markdown inline/reference and MDX `href` links; normalize `/docs/index`, query/hash/trailing slash; validate fragments and `/img/**`; reject content `import`/`export`; permit only:

```js
new Set(["Callout","Steps","Step","Files","Folder","File","Tabs","Tab",
  "DocumentLinkGrid","DocumentLinkGroup","DocumentLinkCard"])
```

- [ ] **Step 4: Copy immutable source content/assets outside `template/`**

```bash
mkdir -p apps/web/src/features/documents/content apps/web/public/img/branding
cp -R template/src/features/documents-system/content/. apps/web/src/features/documents/content/
cp template/public/img/branding/template_logo_nb_s.png apps/web/public/img/branding/
cp template/public/img/branding/web-app-manifest-512x512.png apps/web/public/img/branding/
```

- [ ] **Step 5: Configure MDX and generate artifacts**

Compose existing next-intl with `createMDX({ extension: /\.(md|mdx)$/, options: { remarkPlugins: [remarkFrontmatter, remarkGfm] } })`, add Markdown page extensions, and create required empty `useMDXComponents()`.

```bash
cd apps/web
npm run content:generate
npm run content:check
npm run content:test
```

- [ ] **Step 6: Guard and commit**

```bash
git diff --exit-code origin/main...HEAD -- template/
git add apps/web/next.config.ts apps/web/mdx-components.tsx apps/web/scripts \
  apps/web/src/features/documents apps/web/public/img/branding contracts/documents
git commit -m "feat: add localized documentation content pipeline"
```

---

### Task 3: Application Search Semantics

**Files:**
- Create: `apps/api/src/Template.Application/Documents/DocumentSearchModels.cs`
- Create: `apps/api/src/Template.Application/Documents/DocumentSearchText.cs`
- Create: `apps/api/src/Template.Application/Documents/DocumentSearchService.cs`
- Create: `apps/api/src/Template.Application/Documents/Ports/IDocumentSearchIndexProvider.cs`
- Create: `apps/api/tests/Template.Application.Tests/Documents/DocumentSearchTextTests.cs`
- Create: `apps/api/tests/Template.Application.Tests/Documents/DocumentSearchServiceTests.cs`

**Interfaces:**

```csharp
public enum DocumentLocale { En, Ru }
public sealed record DocumentSearchPage(
    string Type, string Title, string Description, string Href,
    string Group, string ParentItem, int Order,
    string SearchText, string TitleText);
public sealed record DocumentSearchHeading(
    string Type, string Title, string Href, string PageTitle,
    string Group, string ParentItem, int Order,
    string SearchText, string TitleText);
public sealed record DocumentSearchLocaleIndex(
    IReadOnlyList<DocumentSearchPage> Pages,
    IReadOnlyList<DocumentSearchHeading> Headings);
public interface IDocumentSearchIndexProvider
{
    DocumentSearchLocaleIndex Get(DocumentLocale locale);
}
public sealed record DocumentSearchRequest(string Query, DocumentLocale Locale);
public sealed record DocumentSearchPageResult(
    string Type, string Title, string Description, string Href,
    string Group, string ParentItem);
public sealed record DocumentSearchHeadingResult(
    string Type, string Title, string Href, string PageTitle,
    string Group, string ParentItem);
public sealed record DocumentSearchResult(
    IReadOnlyList<DocumentSearchPageResult> Pages,
    IReadOnlyList<DocumentSearchHeadingResult> Headings);
public sealed class DocumentSearchService(IDocumentSearchIndexProvider provider)
{
    public DocumentSearchResult Search(DocumentSearchRequest request);
}
```

- [ ] **Step 1: Write normalization/layout/distance tests RED**

```csharp
Assert.Equal("еж api", DocumentSearchText.Normalize("  Ёж, API!  "));
Assert.Contains("api v1", DocumentSearchText.CreateQueryVariants("фзш м1"));
Assert.Equal(1, DocumentSearchText.GetAllowedTypoDistance("search"));
```

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter FullyQualifiedName~DocumentSearchTextTests
```

- [ ] **Step 2: Implement text semantics and run GREEN**

Use Unicode letter/number categories, `ё→е`, exact keyboard strings, mixed-layout rejection and adjacent-transposition Damerau–Levenshtein. Query length remains .NET `string.Length` (UTF-16).

- [ ] **Step 3: Write ranking/bounds tests RED**

```csharp
Assert.Equal("Exact", service.Search(new("exact", DocumentLocale.En)).Pages[0].Title);
Assert.Equal(32, service.Search(new("", DocumentLocale.En)).Pages.Count);
Assert.Empty(service.Search(new("", DocumentLocale.En)).Headings);
Assert.True(service.Search(new("guide", DocumentLocale.En)).Pages.Count <= 8);
```

Test score order 100/90/80/60/40 and equal-score stable `Order`.

- [ ] **Step 4: Implement bounded projection and run GREEN**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter FullyQualifiedName~DocumentSearch
```

Do not expose internal order/normalized text in result records.

- [ ] **Step 5: Commit**

```bash
git add apps/api/src/Template.Application/Documents apps/api/tests/Template.Application.Tests/Documents
git commit -m "feat: add documentation search application service"
```

---

### Task 4: Embedded Infrastructure Index

**Files:**
- Create: `apps/api/src/Template.Infrastructure/Documents/EmbeddedDocumentSearchIndexProvider.cs`
- Create: `apps/api/src/Template.Infrastructure/Documents/DocumentSearchInfrastructureServiceCollectionExtensions.cs`
- Modify: `apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj`
- Create: `apps/api/tests/Template.Api.Tests/Documents/EmbeddedDocumentSearchIndexProviderTests.cs`

**Interfaces:** singleton implementation of Task 3 `IDocumentSearchIndexProvider` and `AddDocumentSearchInfrastructure()`.

- [ ] **Step 1: Write provider tests RED**

```csharp
Assert.Equal(54, provider.Get(DocumentLocale.En).Pages.Count);
Assert.Contains(provider.Get(DocumentLocale.Ru).Pages, p => p.Href == "/docs/api/api-v1");
Assert.Throws<InvalidDataException>(() => Parse("{\"schemaVersion\":2}"));
```

Also test duplicate JSON properties, missing fields and immutable collections.

- [ ] **Step 2: Embed and implement strict one-time parsing**

```xml
<EmbeddedResource Include="../../../../contracts/documents/search-index.json"
                  LogicalName="Template.Documents.SearchIndex.v1.json" />
```

Use `AllowDuplicateProperties=false`, require schema 1 and exact `en`/`ru`, reject nulls, freeze arrays, and never access a repository path at runtime.

- [ ] **Step 3: Run GREEN**

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~EmbeddedDocumentSearchIndexProviderTests
```

- [ ] **Step 4: Commit**

```bash
git add apps/api/src/Template.Infrastructure/Documents \
  apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  apps/api/tests/Template.Api.Tests/Documents
git commit -m "feat: embed documentation search index"
```

---

### Task 5: Anonymous Search REST Endpoint and OpenAPI

**Files:**
- Create: `apps/api/src/Template.Api/Features/Documents/{DocumentSearchContracts,DocumentSearchEndpointBoundary,DocumentSearchEndpointModule}.cs`
- Modify: `apps/api/src/Template.Api/{ApiHost.cs,Endpoints/EndpointModuleExtensions.cs,appsettings.json,appsettings.Local.example.json}`
- Modify: `apps/api/src/Template.Api/OpenApi/OpenApiServiceCollectionExtensions.cs`
- Create: `apps/api/src/Template.Api/OpenApi/DocumentSearchContractOperationTransformer.cs`
- Create: `apps/api/src/Template.Api/OpenApi/DocumentSearchContractSchemaTransformer.cs`
- Create: `apps/api/tests/Template.Api.Tests/Documents/{DocumentSearchEndpointTests,DocumentSearchFailureTests}.cs`
- Modify: `apps/api/tests/Template.Api.Tests/OpenApiContractTests.cs`, `contracts/openapi/v1.json`

**Interfaces:**

```csharp
internal sealed class DocumentSearchOptions
{
    public const string SectionName = "Documents";
    public string DefaultLocale { get; init; } = "en";
}
internal sealed record DocumentSearchResponse(
    IReadOnlyList<DocumentSearchPageResponse> Pages,
    IReadOnlyList<DocumentSearchHeadingResponse> Headings);
```

Route: `GET /api/v1/documents-system/search?q=&locale=`, operation `SearchDocumentsSystem`, explicit `AllowAnonymous()`.

- [ ] **Step 1: Write endpoint tests RED**

```csharp
Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
Assert.Equal("no-store", anonymous.Headers.CacheControl?.ToString());
Assert.Equal(32, empty.Data.Pages.Count);
Assert.Empty(empty.Data.Headings);
Assert.True(typed.Data.Pages.Count <= 8 && typed.Data.Headings.Count <= 8);
Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
Assert.Equal(HttpStatusCode.BadRequest, invalidLocale.StatusCode);
```

Also prove valid/invalid cookies and `x-api-key` do not alter results, and cover English, Russian and `фзш м1`.

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~DocumentSearchEndpointTests
```

Expected: 404.

- [ ] **Step 2: Register services/options and implement boundary**

```csharp
builder.Services.Configure<DocumentSearchOptions>(
    builder.Configuration.GetSection(DocumentSearchOptions.SectionName));
builder.Services.AddDocumentSearchInfrastructure();
builder.Services.AddSingleton<DocumentSearchService>();
```

Use `Documents:DefaultLocale=en`; absent/invalid configured default resolves to `en`. Missing query locale uses that default, explicit blank/unknown locale is `validation_failed`. Trim `q` before `string.Length > 120`. Set no-store on all search responses.

- [ ] **Step 3: Map endpoint and run GREEN**

Map on `context.VersionedApi.MapGroup("/documents-system")`, override inherited auth with `AllowAnonymous`, return `ApiResponse<DocumentSearchResponse>`, and declare typed validation/public problems.

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~DocumentSearchEndpointTests
```

- [ ] **Step 4: Write safe failure test RED, then implement mapping**

Inject a provider that throws `InvalidOperationException("sensitive fixture text")`. Assert `500 application/problem+json`, stable target fields and no fixture/query/source/body disclosure in payload or logs.

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~DocumentSearchFailureTests
```

- [ ] **Step 5: Add exact OpenAPI tests and minimal transformers**

Assert empty security; optional `q` maxLength 120; optional locale enum `en,ru`; required `data/pages/headings` and result fields; 200/400/406/500. Keep both documents transformers narrowly scoped to this operation and never loosen global schemas.

```bash
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter FullyQualifiedName~OpenApiContractTests
```

- [ ] **Step 6: Export twice and commit**

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore -p:OpenApiGenerateDocuments=true
cp contracts/openapi/v1.json /tmp/iteration8-openapi.json
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore -p:OpenApiGenerateDocuments=true
cmp /tmp/iteration8-openapi.json contracts/openapi/v1.json
git add apps/api/src/Template.Api apps/api/tests/Template.Api.Tests contracts/openapi/v1.json
git commit -m "feat: expose public documentation search API"
```

---

### Task 6: Generated SDK and Browser Adapter

**Files:**
- Regenerate: `apps/web/src/lib/api/generated/**`
- Create: `apps/web/src/lib/api/documents/browser/search-documents.ts`
- Create: `apps/web/test/lib/api/documents/search-documents.test.ts`
- Modify: boundary checker and node test
- Create: `apps/web/test/contracts/documents-boundaries.test.ts`

**Interfaces:**

```ts
export type SearchDocumentsInput = Readonly<{
  query: string;
  locale: "en" | "ru";
  signal?: AbortSignal;
}>;
export async function searchDocuments(
  input: SearchDocumentsInput,
): Promise<ApiResult<DocumentSearchResponse>>;
```

- [ ] **Step 1: Regenerate and inspect SDK**

```bash
cd apps/web
npm run api:generate
rg -n "searchDocumentsSystem" src/lib/api/generated
```

- [ ] **Step 2: Write adapter/boundary tests RED**

```ts
expect(searchDocumentsSystem).toHaveBeenCalledWith({
  client: expect.anything(), query: { q: "api", locale: "en" }, signal,
});
```

Assert abort propagation, safe failure normalization, no raw fetch/handwritten DTO/Next `/api` handler.

- [ ] **Step 3: Implement generated-only adapter and guards**

Use `createBrowserApiClient`, generated `searchDocumentsSystem`, and existing `normalizeApiFailure`. Add operation presence and document DTO names to the boundary checker.

- [ ] **Step 4: Run GREEN and commit**

```bash
cd apps/web
npm run api:check
npm run boundaries:check
npm test -- --runInBand test/lib/api/documents/search-documents.test.ts \
  test/contracts/documents-boundaries.test.ts
cd ../..
git add apps/web/src/lib/api/generated apps/web/src/lib/api/documents \
  apps/web/test/lib/api/documents apps/web/test/contracts apps/web/scripts/check-boundaries*
git commit -m "feat: add generated documentation search client"
```

---

### Task 7: Public Registry and Routes

**Files:**
- Create: `apps/web/src/features/documents/{documents-types,documents-registry,documents-routes,documents-navigation}.ts`
- Create: `apps/web/src/app/(documents)/docs/{page,loading,error}.tsx`
- Create: `apps/web/src/app/(documents)/docs/[...slug]/page.tsx`
- Create: registry and page Jest tests.

**Interfaces:**

```ts
export function getDocumentsRegistry(locale: AppLocale): DocumentsRegistry;
export function findPublishedDocument(locale: AppLocale, canonicalUrl: string): DocumentInfo | undefined;
export function buildDocumentStaticParams(): Array<{ slug: string[] }>;
export async function importDocument(document: DocumentInfo): Promise<{ default: ComponentType<MDXProps> }>;
```

- [ ] **Step 1: Write registry/navigation tests RED**

```ts
expect(getDocumentsRegistry("en").visibleDocuments).toHaveLength(54);
expect(findPublishedDocument("ru", "workspace")?.contentLocale).toBe("ru");
expect(buildDocumentStaticParams()).not.toContainEqual({ slug: ["index"] });
expect(buildDocumentStaticParams().some(({slug}) => slug.join("/").endsWith(".ru"))).toBe(false);
```

- [ ] **Step 2: Implement facade and stable navigation**

Read only the generated registry, resolve the fixed locale, filter production-visible variants, group/order sidebar and previous/next, and keep generated types behind UI-only records.

- [ ] **Step 3: Write page tests RED and implement routes**

Assert root loads `index`, catch-all joins slug, `dynamicParams=false`, static params contain `api/api-v1`, and missing/unpublished calls `notFound`. Render a semantic temporary article until Task 9.

- [ ] **Step 4: Run GREEN and commit**

```bash
cd apps/web
npm test -- --runInBand test/features/documents/documents-registry.test.ts \
  test/app/documents-pages.test.tsx
npm run typecheck
cd ../..
git add apps/web/src/features/documents apps/web/src/app/'(documents)' apps/web/test
git commit -m "feat: add public documentation routes"
```

---

### Task 8: Documentation Shell and Navigation

**Files:**
- Create: `apps/web/src/components/documents/{documents-shell,documents-header,documents-breadcrumb,documents-sidebar,documents-page-navigation}.tsx`
- Create: `apps/web/src/app/(documents)/docs/layout.tsx`
- Create: `apps/web/src/messages/documents.{en,ru}.json`, register in i18n.
- Create: shell and message-shape tests.

**Interfaces:** public docs-only shell with one `main#main-content`, desktop/mobile sidebar, active parent, breadcrumb, home/theme/search slots and previous/next links.

- [ ] **Step 1: Add paired messages and failing shape tests**

```json
{"navigation":{"label":"Documentation","home":"Home"},
 "sidebar":{"title":"Documentation","open":"Open navigation","close":"Close navigation"},
 "page":{"previous":"Previous document","next":"Next document","onThisPage":"On this page"},
 "search":{"open":"Search docs","placeholder":"Search docs...","unavailable":"Search is temporarily unavailable"}}
```

Add equivalent Russian keys and recursive shape equality.

- [ ] **Step 2: Write shell/sidebar tests RED**

```ts
expect(screen.getByRole("main")).toHaveAttribute("id", "main-content");
expect(screen.getByRole("link", { name: "API v1 reference" })).toHaveAttribute("aria-current", "page");
```

Test active parent after pathname change and mobile close after selection.

- [ ] **Step 3: Implement docs-specific shell**

Use existing Button/Dialog/ThemeSwitcher, component-memory drawer state, and generated navigation. Do not import `(site)`, session/workspace loaders or browser storage.

- [ ] **Step 4: Run GREEN and commit**

```bash
cd apps/web
npm test -- --runInBand test/components/documents/documents-shell.test.tsx test/i18n
npm run boundaries:check
npm run typecheck
cd ../..
git add apps/web/src/components/documents apps/web/src/app/'(documents)'/docs/layout.tsx \
  apps/web/src/messages apps/web/src/i18n/messages.ts apps/web/test
git commit -m "feat: add documentation navigation shell"
```

---

### Task 9: Article, TOC and Closed MDX Components

**Files:**
- Create: `apps/web/src/features/documents/documents-heading-tools.ts`.
- Create: `apps/web/src/components/documents/documents-page.tsx`.
- Create: `apps/web/src/components/documents/documents-page-meta.tsx`.
- Create: `apps/web/src/components/documents/documents-page-toc.tsx`.
- Create: `apps/web/src/components/documents/documents-scroll-spy.ts`.
- Create: `apps/web/src/components/documents/documents-copy-button.tsx`.
- Create: `apps/web/src/components/documents/mdx/documents-mdx-components.tsx`.
- Create: `apps/web/src/components/documents/mdx/documents-link-grid.tsx`.
- Create: `apps/web/src/components/ui/tabs.tsx`.
- Modify: `apps/web/src/app/(documents)/docs/page.tsx`.
- Modify: `apps/web/src/app/(documents)/docs/[...slug]/page.tsx`.
- Modify: `apps/web/mdx-components.tsx`, `apps/web/src/app/globals.css`.
- Create: `apps/web/test/features/documents/documents-heading-tools.test.ts`.
- Create: `apps/web/test/components/documents/documents-page.test.tsx`.
- Create: `apps/web/test/components/documents/documents-mdx-components.test.tsx`.

**Interfaces:**

```ts
export function slugifyDocumentHeadingText(text: string): string;
export function createUniqueDocumentHeadingId(text: string, seen: Map<string, number>): string;
export function createDocumentMdxComponents(document: DocumentInfo): MDXComponents;
```

- [ ] **Step 1: Write heading/TOC/meta tests RED**

```ts
expect(createUniqueDocumentHeadingId("Раздел", seen)).toBe("раздел");
expect(createUniqueDocumentHeadingId("Раздел", seen)).toBe("раздел-2");
expect(screen.getByText("2026-07-23")).toBeInTheDocument();
```

Test malformed hash safety, scroll container, `toc=false`, date-only and fallback marker.

- [ ] **Step 2: Implement article/TOC/scroll behavior**

Render group/parent/purpose/date/author/version/reading/status/visibility/language, generated initial headings, duplicate ID normalization and docs-scroll-container tracking.

- [ ] **Step 3: Write MDX tests RED and implement closed map**

Cover `Callout`, `Steps/Step`, `Files/Folder/File`, `Tabs/Tab`, `DocumentLinkGrid/Group/Card`, headings, safe links, images, tables and copy controls. Production broken/unpublished links must not be active; external links use safe attributes.

- [ ] **Step 4: Run GREEN and commit**

```bash
cd apps/web
npm test -- --runInBand test/features/documents/documents-heading-tools.test.ts \
  test/components/documents/documents-page.test.tsx \
  test/components/documents/documents-mdx-components.test.tsx test/app/documents-pages.test.tsx
npm run typecheck
cd ../..
git add apps/web/mdx-components.tsx apps/web/src/features/documents \
  apps/web/src/components/documents apps/web/src/components/ui/tabs.tsx \
  apps/web/src/app/'(documents)' apps/web/src/app/globals.css apps/web/test
git commit -m "feat: render documentation articles and MDX components"
```

---

### Task 10: Search Dialog UI

**Files:**
- Create: `apps/web/src/components/documents/{documents-search,documents-search-results}.tsx`
- Modify: `apps/web/src/components/documents/documents-header.tsx`
- Create: `apps/web/test/components/documents/documents-search.test.tsx`

**Interfaces:** consumes Task 6 `searchDocuments`; produces accessible `Ctrl/⌘+K` dialog with 250 ms debounce, cancellation, stale protection and canonical navigation.

- [ ] **Step 1: Write keyboard/debounce/result tests RED**

```ts
fireEvent.keyDown(document, { key: "k", ctrlKey: true });
expect(screen.getByRole("dialog")).toBeVisible();
fireEvent.change(screen.getByRole("searchbox"), { target: { value: "api" } });
jest.advanceTimersByTime(249);
expect(searchDocuments).not.toHaveBeenCalled();
jest.advanceTimersByTime(1);
expect(searchDocuments).toHaveBeenCalledWith(expect.objectContaining({ query: "api", locale: "en" }));
```

Also test page/heading groups, maxLength 120, escape/reset, empty/error copy, abort and stale older success.

- [ ] **Step 2: Implement safe async state**

Use existing Dialog/Button/Input and semantic listbox/options. Create an `AbortController` and generation number per request; check both before every state write. Abort is silent; other failures use localized unavailable copy and never render raw Problem Details.

- [ ] **Step 3: Implement navigation and header integration**

On selection close/reset then `router.push(result.href as Route)`. Disable old results during replacement search. Do not log raw query/payload.

- [ ] **Step 4: Run GREEN and commit**

```bash
cd apps/web
npm test -- --runInBand test/components/documents/documents-search.test.tsx
npm run boundaries:check
npm run lint
npm run typecheck
cd ../..
git add apps/web/src/components/documents apps/web/test/components/documents/documents-search.test.tsx
git commit -m "feat: add documentation search dialog"
```

---

### Task 11: Metadata, OG Images and Sitemap

**Files:**
- Create: `apps/web/src/lib/public-origin.ts`
- Create: `apps/web/src/app/(documents)/docs/{opengraph-image.tsx,twitter-image.ts}`
- Create: `apps/web/src/app/(documents)/docs/og/[...slug]/route.ts`
- Modify: `apps/web/src/app/(documents)/docs/page.tsx`.
- Modify: `apps/web/src/app/(documents)/docs/[...slug]/page.tsx`.
- Modify: `apps/web/src/app/layout.tsx`.
- Create: `apps/web/src/app/sitemap.ts`
- Modify: `apps/web/scripts/check-boundaries.mjs`.
- Modify: `apps/web/scripts/check-boundaries.node-test.mjs`.
- Create: `apps/web/test/app/documents-metadata.test.ts`.
- Create: `apps/web/test/app/sitemap.test.ts`.

**Interfaces:**

```ts
export function resolvePublicOrigin(value = process.env.APP_PUBLIC_ORIGIN): URL;
```

Allow absolute HTTP(S) without credentials/query/fragment; default to `http://localhost:3000` when unset.

- [ ] **Step 1: Write origin/metadata/sitemap tests RED**

```ts
expect(urls).toContain("http://localhost:3000/docs");
expect(urls).toContain("http://localhost:3000/docs/api/api-v1");
expect(urls.some((url) => /\.(en|ru)(?:\/|$)/u.test(url))).toBe(false);
```

Assert localized title/description/OG URL, 54 canonical entries once, editedAt, weekly, root 0.8/article 0.6.

- [ ] **Step 2: Implement standard metadata images**

Use generated registry and `ImageResponse` at 1200×630 PNG, set root `metadataBase`, and perform no external fetch.

- [ ] **Step 3: Write exact OG route/boundary tests RED**

Known slug/locales return PNG; unknown returns 404; invalid locale 400. Allow exactly:

```js
new Set(["src/app/(documents)/docs/og/[...slug]/route.ts"])
```

Prove a second route handler and all `src/app/api/**` handlers still fail.

- [ ] **Step 4: Implement exact OG route and sitemap**

Use generated production-visible registry, static params and safe locale parsing. Sitemap emits absolute canonical entries only.

- [ ] **Step 5: Run GREEN/build and commit**

```bash
cd apps/web
npm test -- --runInBand test/app/documents-metadata.test.ts test/app/sitemap.test.ts
npm run boundaries:check
npm run typecheck
rm -rf .next
APP_PUBLIC_ORIGIN=http://localhost:3000 npm run build
test -f .next/standalone/server.js
cd ../..
git add apps/web/src/lib/public-origin.ts apps/web/src/app apps/web/scripts/check-boundaries* apps/web/test/app
git commit -m "feat: add documentation metadata and sitemap"
```

---

### Task 12: Rewrite Account and Workspace Content

**Files:**
- Modify: `apps/web/src/features/documents/content/account/**` (8 files).
- Modify: `apps/web/src/features/documents/content/workspace/**` (16 files).
- Create: `apps/web/test/features/documents/documents-content-policy.test.ts`
- Regenerate: registry and search index.

**Interfaces:** paired current guidance for iterations 3–6; no prescriptive Prisma/Better Auth/Server Actions.

- [ ] **Step 1: Write content-policy test RED**

```ts
expect(accountEn).toContain("ASP.NET Core");
expect(accountEn).toContain("HttpOnly");
expect(accountRu).toContain("ASP.NET Core");
expect(workspaceEn).toContain("/api/v1/organizations");
expect(workspaceRu).toContain("/api/v1/organizations");
expect(allCurrentText).not.toMatch(/use Prisma directly|call a Server Action|Better Auth owns/iu);
```

- [ ] **Step 2: Rewrite four account locale pairs**

```text
index: REST settings and secure cookie
profile-connections: profile API, verified email, five OAuth providers
sessions-security: opaque cursors, revoke one/all others, CSRF
delete-account: survivor/ownership checks and hard-delete REST flow
```

- [ ] **Step 3: Rewrite eight workspace locale pairs**

```text
index/create-switch: organization model and generated REST create/list/set-active
settings/email-domains: PATCH/DELETE, slug/domain rules, CSRF/acknowledgement
members-roles: owner/admin/member authorization and bounded pages
invitations/teams: 48-hour decisions, non-disclosure, team/member/candidate REST
no-workspace: API-backed onboarding without browser/database shortcuts
```

- [ ] **Step 4: Regenerate, run GREEN and commit**

```bash
cd apps/web
npm run content:generate
npm run content:check
npm run content:test
npm test -- --runInBand test/features/documents/documents-content-policy.test.ts
cd ../..
git add apps/web/src/features/documents/content/{account,workspace} \
  apps/web/src/features/documents/generated contracts/documents \
  apps/web/test/features/documents/documents-content-policy.test.ts
git commit -m "docs: update account and workspace documentation"
```

---

### Task 13: Rewrite API and Application Content

**Files:**
- Modify: `apps/web/src/features/documents/content/api/**` (8 files).
- Modify: `apps/web/src/features/documents/content/application/**` (12 files).
- Extend content-policy test; regenerate both artifacts.

**Interfaces:** current guidance for iterations 1–4 and 7.

- [ ] **Step 1: Extend policy test RED**

```ts
expect(apiV1En).toContain("x-api-key");
expect(apiV1En).toContain("application/problem+json");
expect(apiV1Ru).toContain("x-api-key");
expect(applicationEn).toContain("generated REST SDK");
expect(applicationRu).toContain("ASP.NET Core");
```

- [ ] **Step 2: Rewrite four API locale pairs**

```text
index: cookie management versus machine x-api-key
api-keys: reveal-once, SHA-256 storage, owners, rotate/revoke
api-v1: supported reads, {data}, Problem Details, opaque cursors
permissions-rate-limits: closed scopes, tenant isolation, fixed windows
```

- [ ] **Step 3: Rewrite six application locale pairs**

```text
index/localization: separate REST UI and fixed locale-neutral routing
oauth-providers/runtime-security: ASP.NET/OpenIddict, HttpOnly, CSRF, no browser bearer
settings-shell: current REST loaders/mutations, final shell deferred to iteration 9
caching: current no-store/session rules, Redis explicitly out of scope
```

- [ ] **Step 4: Regenerate, run GREEN and commit**

```bash
cd apps/web
npm run content:generate && npm run content:check && npm run content:test
npm test -- --runInBand test/features/documents/documents-content-policy.test.ts
cd ../..
git add apps/web/src/features/documents/content/{api,application} \
  apps/web/src/features/documents/generated contracts/documents apps/web/test/features/documents
git commit -m "docs: update API and application documentation"
```

---

### Task 14: Rewrite Developer and General Content

**Files:**
- Modify: `apps/web/src/features/documents/content/developers/**` (14 files).
- Modify: `apps/web/src/features/documents/content/general/**` (10 files).
- Extend policy test; regenerate both artifacts.

**Interfaces:** accurate target architecture/development/authoring/glossary content.

- [ ] **Step 1: Extend policy tests RED**

```ts
expect(featureSliceEn).toContain("Domain");
expect(featureSliceEn).toContain("Application");
expect(featureSliceEn).toContain("Infrastructure");
expect(featureSliceEn).toContain("Api");
expect(serverBoundaryEn).toContain("REST");
expect(authoringEn).toContain("npm run content:check");
expect(authoringRu).toContain("npm run content:check");
```

- [ ] **Step 2: Rewrite seven developer locale pairs**

Cover target layout; inward dependencies; test-first Minimal API/OpenAPI/SDK; why the UI uses REST rather than Server Actions; ASP.NET local automation; initialized-only OpenSpec; release/documentation workflow. Preserve canonical `/docs/developers/server-actions` as an explicit legacy-to-target explanation.

- [ ] **Step 3: Rewrite five general locale pairs**

Document strict frontmatter, paired locales, closed MDX components, links/images, generate/check commands and target glossary/quick-start. Preserve every custom component in sample MDX as a live rendering fixture.

- [ ] **Step 4: Regenerate, compile, run GREEN and commit**

```bash
cd apps/web
npm run content:generate && npm run content:check && npm run content:test
npm test -- --runInBand test/features/documents/documents-content-policy.test.ts \
  test/components/documents/documents-mdx-components.test.tsx
npm run typecheck
cd ../..
git add apps/web/src/features/documents/content/{developers,general} \
  apps/web/src/features/documents/generated contracts/documents apps/web/test/features/documents
git commit -m "docs: update developer and authoring documentation"
```

---

### Task 15: Rewrite Root and Historical Content

**Files:**
- Modify: `apps/web/src/features/documents/content/index.{en,ru}.mdx`.
- Modify: `apps/web/src/features/documents/content/history/**` (38 files).
- Extend content-policy test; regenerate both artifacts.

**Interfaces:** current landing page and factual history that visibly labels the former full-stack Next.js runtime as legacy.

- [ ] **Step 1: Add landing/history policy tests RED**

```ts
expect(rootEn).toContain("ASP.NET Core");
expect(rootRu).toContain("ASP.NET Core");
expect(rootEn).toContain("/docs/developers");
expect(historyEn).toMatch(/legacy/iu);
expect(historyRu).toMatch(/прежн|legacy/iu);
expect(historyEn).toContain("migration");
expect(historyRu).toContain("миграц");
```

- [ ] **Step 2: Rewrite root MDX pair**

Retain all six link groups and closed components; present ASP.NET Core 10 API plus separate REST-only Next UI and link current account/workspace/API/application/developer/history guidance.

- [ ] **Step 3: Update release and weekly history pairs**

Preserve version/date/event facts and links. Add an early visible statement that each entry records the former full-stack implementation and point current readers to `/docs/application` and `/docs/developers`.

- [ ] **Step 4: Regenerate/build/run GREEN and commit**

```bash
cd apps/web
npm run content:generate && npm run content:check && npm run content:test
npm test -- --runInBand test/features/documents/documents-content-policy.test.ts
npm run typecheck
rm -rf .next
APP_PUBLIC_ORIGIN=http://localhost:3000 npm run build
cd ../..
git add apps/web/src/features/documents/content/index.*.mdx \
  apps/web/src/features/documents/content/history apps/web/src/features/documents/generated \
  contracts/documents apps/web/test/features/documents
git commit -m "docs: update documentation landing and history"
```

---

### Task 16: Full-Stack Documentation E2E

**Files:**
- Create: `apps/web/e2e/documents.spec.ts`
- Modify: `apps/web/playwright.config.ts`

**Interfaces:** anonymous black-box acceptance against the existing API/web E2E hosts.

- [ ] **Step 1: Configure both hosts consistently**

```ts
// API env
Documents__DefaultLocale: "en"
// Web env
PUBLIC_DEFAULT_LOCALE: "en"
APP_PUBLIC_ORIGIN: webOrigin
```

- [ ] **Step 2: Write public route/navigation scenario RED**

```ts
await page.goto("/docs/api/api-v1");
await expect(page).toHaveURL(/\/docs\/api\/api-v1$/);
await expect(page.getByRole("heading", { level: 1 })).toContainText("API");
```

Cover anonymous `/docs`, deep route, no locale suffix/login redirect, sidebar/current link/previous-next/TOC and unknown 404.

```bash
cd apps/web
npx playwright test e2e/documents.spec.ts --project=chromium --grep "public routes"
```

- [ ] **Step 3: Write search/OG scenarios and make focused E2E GREEN**

Cover Ctrl/Meta+K, `api` page result, heading hash navigation, empty search, PNG content type and unknown OG 404. For each integration defect, add the smallest focused regression test before changing production code.

```bash
cd apps/web
npx playwright test e2e/documents.spec.ts --project=chromium
```

- [ ] **Step 4: Run full Chromium and commit**

```bash
cd apps/web
npm run e2e:install
npm run e2e
cd ../..
git add apps/web/e2e/documents.spec.ts apps/web/playwright.config.ts apps/web/e2e/support
git commit -m "test: cover public documentation journeys"
```

---

### Task 17: Durable Documentation and Complete Acceptance

**Files:**
- Modify: `docs/{api-conventions,web-conventions,aspnetcore-migration-plan}.md`
- Create: `docs/documentation-authoring.md`
- Update completed plan checkboxes/evidence in this file.

**Interfaces:** exact observed architecture, authoring rules, mapping, command results, differences and next gates.

- [ ] **Step 1: Run focused final suites and capture counts**

```bash
dotnet test apps/api/tests/Template.Application.Tests/Template.Application.Tests.csproj \
  --no-restore --filter FullyQualifiedName~DocumentSearch
dotnet test apps/api/tests/Template.Api.Tests/Template.Api.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~DocumentSearch|FullyQualifiedName~OpenApiContractTests'
cd apps/web
npm run content:check
npm run content:test
npm test -- --runInBand test/features/documents test/components/documents \
  test/app/documents-pages.test.tsx test/app/documents-metadata.test.ts \
  test/app/sitemap.test.ts test/lib/api/documents test/contracts/documents-boundaries.test.ts
```

- [ ] **Step 2: Run mandatory .NET gates**

```bash
cd ../..
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet format Template.sln --no-restore --verify-no-changes
dotnet list Template.sln package --vulnerable --include-transitive
```

- [ ] **Step 3: Run deterministic content/OpenAPI/SDK gates**

```bash
cd apps/web
npm run content:generate
npm run content:check
cd ../..
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore -p:OpenApiGenerateDocuments=true
cp contracts/openapi/v1.json /tmp/iteration8-final-openapi.json
dotnet build apps/api/src/Template.Api/Template.Api.csproj --no-restore -p:OpenApiGenerateDocuments=true
cmp /tmp/iteration8-final-openapi.json contracts/openapi/v1.json
cd apps/web
npm run api:check
```

- [ ] **Step 4: Run complete web/build/security/browser gates**

```bash
npm ci
npm run content:check
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
rm -rf .next
APP_PUBLIC_ORIGIN=http://localhost:3000 npm run build
test -f .next/standalone/server.js
npm run audit:prod
npm audit --json > /tmp/iteration8-final-npm-audit.json || true
npm run e2e
```

- [ ] **Step 5: Run repository guards**

```bash
cd ../..
git diff --check
git diff --exit-code origin/main...HEAD -- template/
test ! -d openspec/changes || \
  test -z "$(find openspec/changes -mindepth 1 -maxdepth 1 ! -name archive -print -quit)"
git status --short --branch
```

- [ ] **Step 6: Write durable decisions and evidence**

Record anonymous search, neutral artifact, MDX/OG boundary exception and SDK-only browser calls in API/web conventions. Authoring docs must state exact frontmatter, locale/status, components, links/images and generate/check rules. Migration plan must contain scope, reference mapping, actual test counts/commands, known differences, immutable-reference evidence and iteration 9–12 out-of-scope items. Do not claim an unobserved review result.

- [ ] **Step 7: Commit durable docs**

```bash
git add docs/api-conventions.md docs/web-conventions.md docs/documentation-authoring.md \
  docs/aspnetcore-migration-plan.md \
  docs/superpowers/plans/2026-08-02-public-documentation-system.md
git commit -m "docs: record public documentation acceptance"
```

---

### Task 18: Ready PR and Automatic Review Loop

**Files:** modify only actionable-review files; append review evidence only after it is observed.

**Interfaces:** pushed ready PR whose exact latest head has a fresh clean review, zero unresolved actionable threads and mergeable checks.

- [ ] **Step 1: Final branch review**

```bash
git status --short --branch
git log --oneline origin/main..HEAD
git diff --stat origin/main...HEAD
git diff --exit-code origin/main...HEAD -- template/
git diff --check
```

- [ ] **Step 2: Push and create ready PR**

Create `/tmp/iteration8-pr-body.md` containing mapping, compiler/REST/auth/validation/errors/pagination/transactions, exact evidence, differences and out-of-scope list.

```bash
git push -u origin codex/iteration-8-public-documentation
gh pr create --base main --head codex/iteration-8-public-documentation \
  --title "Implement public documentation system" \
  --body-file /tmp/iteration8-pr-body.md
gh pr view --json number,url,isDraft,state,mergeable,mergeStateStatus,headRefOid
```

Do not pass `--draft`; require `isDraft=false`.

- [ ] **Step 3: Wait for and inspect automatic review**

Read checks, comments, reviews, threads and reviewed SHA through GitHub/`gh`; never fabricate a self-review.

```bash
gh pr checks --watch
gh pr view --json number,url,headRefOid,reviews,comments,statusCheckRollup
```

- [ ] **Step 4: Fix every actionable finding test-first**

Invoke `superpowers:receiving-code-review`. Add a failing regression test for behavior defects, run affected Task 17 gates, update both locales for semantic content fixes, regenerate artifacts, and reject only design-conflicting suggestions with evidence.

- [ ] **Step 5: Commit, push, resolve and repeat**

```bash
git diff --name-only --diff-filter=ACMR
git add -- $(git diff --name-only --diff-filter=ACMR)
git commit -m "fix: address documentation review findings"
git push
```

Resolve fixed threads and obtain a fresh review for the new head; repeat Steps 3–5 until clean.

- [ ] **Step 6: Record final observed clean head**

If tracked review evidence changes, commit/push it and require another fresh review. Finish:

```bash
git diff --check
git diff --exit-code origin/main...HEAD -- template/
git status --short --branch
gh pr view --json number,url,isDraft,state,mergeable,mergeStateStatus,headRefOid,statusCheckRollup
```

Complete only when the exact final pushed head has a fresh clean automatic review and zero unresolved actionable threads.

---

## Spec Coverage Self-Check

| Design requirement | Tasks |
| --- | --- |
| Deterministic compiler and neutral artifact | 1–2 |
| Metadata, locales, publication, links, headings, MDX, images | 1–2 |
| Application search semantics | 3 |
| Embedded Infrastructure index | 4 |
| Anonymous REST, validation, no-store, Problem Details, OpenAPI | 5 |
| Generated SDK/browser boundary | 6 |
| Public routes/registry | 7 |
| Docs shell/navigation | 8 |
| Article/TOC/MDX | 9 |
| Debounced/cancellable search UI | 10 |
| Metadata/OG/sitemap | 11 |
| Target-architecture en/ru corpus | 12–15 |
| Anonymous E2E | 16 |
| Durable docs/all gates | 17 |
| Ready PR/clean automatic review | 18 |
| No DB/schema/transactions/Redis/CMS/OpenSpec/reference edits | Global constraints, 2, 4, 17 |

Execution mode is fixed by the user: use `superpowers:subagent-driven-development`, dispatch a fresh implementation subagent for each task, perform two-stage review between tasks, and keep controller ownership of integration, full acceptance, push, PR and review-thread state.
