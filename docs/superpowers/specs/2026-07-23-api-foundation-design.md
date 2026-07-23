# Итерация 1: API foundation и контрактная дисциплина

**Дата:** 2026-07-23  
**Статус:** дизайн согласован пользователем  
**Долгосрочная дорожная карта:** [`../../aspnetcore-migration-plan.md`](../../aspnetcore-migration-plan.md)

## 1. Цель

Итерация 1 превращает минимальный ASP.NET Core 10 host из итерации 0 в предсказуемую платформу для будущих предметных API. Она фиксирует единые HTTP-контракты, validation и authorization boundaries, observability, health probes, OpenAPI и integration-test fixture до появления БД, Identity, Next.js UI и продуктовых endpoints.

Итерация не переносит предметную область из reference. Она создаёт только постоянные технические endpoints, необходимые для проверки инфраструктурных соглашений и для API-status smoke-сценария будущего UI.

## 2. Изученный контекст

Перед проектированием проверены:

- корневой `AGENTS.md`;
- `docs/aspnetcore-migration-plan.md`;
- текущая .NET 10 solution и существующий `HealthEndpointTests`;
- недавние bootstrap-коммиты;
- reference `template/src/app/api/health/route.ts`;
- route classification и session protection в `template/src/features/routes.ts` и `template/src/proxy.ts`;
- validation, action result и error conventions в `template/src/lib/actions.ts`, `template/src/types/actions.ts` и `template/src/features/api-keys/api-keys-errors.ts`;
- reference structured logging в `template/src/lib/logger.ts`;
- API v1 route handlers, API-key authorization tests, route configuration tests и security E2E;
- `template/prisma/schema.prisma`;
- reference E2E readiness wiring в `template/e2e/support/config.ts`.

Reference использует `{ "data": ... }` для успешного public API и `{ "error": { "code", "message" } }` для обработанных ошибок. Новый API сохраняет success envelope, но намеренно переходит на RFC Problem Details для ошибок.

Актуальные решения сверены с официальной документацией:

- [Validation in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/validation/overview?view=aspnetcore-10.0);
- [Handle errors in ASP.NET Core APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0);
- [OpenAPI support in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0);
- [Cookie authentication behavior for API endpoints](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/api-endpoint-auth?view=aspnetcore-10.0);
- [Health checks in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0);
- [Logging in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0).

## 3. Scope

### Входит

- RFC Problem Details и стабильные application error codes;
- built-in ASP.NET Core 10 validation для Minimal APIs;
- URL-based API versioning policy;
- явно зарегистрированные endpoint-модули;
- first-party OpenAPI и build-time export;
- secure cookie authentication handler без Identity;
- именованные authorization policies и test-only authentication;
- structured request logging и correlation IDs;
- compatibility health, liveness и readiness endpoints;
- общая `WebApplicationFactory` fixture;
- committed OpenAPI contract и semantic drift check;
- долговременная документация API conventions;
- обновление migration register и acceptance evidence.

### Не входит

- Next.js UI и generated TypeScript client;
- PostgreSQL, EF Core, migrations и seed;
- пользователи, Identity, login, logout и выдача реальной session cookie;
- antiforgery, пока отсутствуют cookie-authenticated mutations;
- OAuth, API keys и product authorization model;
- organizations, workspaces, teams и другие предметные endpoints;
- YARP, Docker, Aspire и production Data Protection key persistence;
- CSP и окончательное владение UI/security headers;
- активный OpenSpec change или spec.

`Template.Domain`, `Template.Application` и `Template.Infrastructure` не получают foundation-код: в этой итерации нет бизнес-правил, use cases, хранилищ или внешних адаптеров. `apps/web` остаётся неизменным.

## 4. Карта соответствий

| Reference | Новый API | Новый UI | Проверка |
| --- | --- | --- | --- |
| `src/app/api/health/route.ts`, `e2e/support/config.ts` | `GET /api/health`, `/api/health/live`, `/api/health/ready` | Вне scope; iteration 2 использует API status | Integration tests статусов, cache headers и JSON |
| `src/features/routes.ts`, `src/proxy.ts` | Защищённая по умолчанию группа `/api/v1`; public system status; protected authenticated probe | Нет | Public 200, anonymous 401, authenticated 200 |
| `src/lib/actions.ts`, `types/actions.ts`, `api-keys-errors.ts` | Built-in validation, `{ data }`, RFC Problem Details | Нет | 400, 401, 403, 404, 405 и 500 contract tests |
| `src/lib/logger.ts` | `ILogger`, request scope, JSON console в production, correlation ID | Нет | Correlation header, Problem Details и captured log scope |
| API-key route/auth tests | Единые HTTP-boundary conventions без переноса API-key domain | Нет | `WebApplicationFactory` integration tests |
| `prisma/schema.prisma` | Нет соответствия в iteration 1 | Нет | Отсутствие EF schema/migrations |

Reference security-header E2E каталогизирован, но не переносится в эту итерацию. Окончательный набор page/API headers должен быть согласован с Next.js UI и single-origin YARP topology в соответствующих итерациях.

## 5. Архитектура API host

`Program.cs` остаётся только composition root. API-код делится на небольшие блоки:

- `Endpoints` — общий контракт endpoint-модуля и его явная регистрация;
- `Errors` — problem codes, Problem Details customization и global exception handler;
- `Authentication` — cookie scheme и именованные policies;
- `Observability` — correlation и request completion logging;
- `OpenApi` — document, operation и schema transformers;
- `Features/Health` — operational probes;
- `Features/System` — технические versioned endpoints и их HTTP DTO.

Endpoint-модули реализуют общий `IEndpointModule`, но перечисляются явно. Reflection scanning и дополнительные DI-scanning библиотеки не используются. Это сохраняет startup предсказуемым и делает состав HTTP surface видимым в одном месте.

Все future consumer endpoints маппятся внутри route group `/api/v1`, который требует authenticated user по умолчанию. Публичный endpoint обязан явно вызвать `AllowAnonymous`. Operational health endpoints и development-only OpenAPI endpoint находятся вне versioned group и также явно помечаются anonymous.

## 6. REST-контракт

### 6.1 Версионирование

- Consumer API использует только URL segment `/api/v1`.
- Query-string, header и media-type versioning не поддерживаются.
- Совместимые additive changes разрешены внутри v1.
- Удаление поля, изменение его смысла или другой breaking change требует нового `/api/v2`.
- Версии должны сосуществовать на период явно задокументированной deprecation.
- Operational `/api/health*` endpoints не версионируются.
- Дополнительная versioning library не добавляется до появления второй реальной версии или требований по version negotiation/reporting.

### 6.2 Endpoints

| Метод и маршрут | Доступ | Контракт |
| --- | --- | --- |
| `GET /api/health` | anonymous | Compatibility alias для readiness |
| `GET /api/health/live` | anonymous | Liveness процесса, без dependency checks |
| `GET /api/health/ready` | anonymous | Checks с тегом `ready` |
| `GET /api/v1/system/status` | anonymous | API status и validation probe |
| `GET /api/v1/system/authenticated` | authenticated | Authorization pipeline probe |
| `GET /api/v1/auth/session` | anonymous | Только зарезервированный future contract; реализация в iteration 3 |

`GET /api/v1/system/status` принимает необязательный query parameter `echo`. Если он передан, его длина должна быть от 1 до 64 символов. Успешный ответ:

```json
{
  "data": {
    "status": "ok",
    "apiVersion": "1",
    "timestamp": "2026-07-23T12:00:00Z",
    "echo": "optional value"
  }
}
```

`GET /api/v1/system/authenticated` возвращает следующий ответ после успешной authentication/authorization:

```json
{
  "data": {
    "status": "authenticated"
  }
}
```

Endpoint не раскрывает claims или данные пользователя. Future frontend определяет пользователя только через session projection из раздела 8.

### 6.3 Success envelope

Обычный успешный JSON всегда имеет форму:

```json
{
  "data": {}
}
```

Endpoint возвращает конкретный typed envelope, чтобы OpenAPI не зависел от неявных или hand-written schemas.

### 6.4 Problem Details

Ошибки `/api/**` возвращаются с content type `application/problem+json`:

```json
{
  "type": "urn:template:problem:validation_failed",
  "title": "Request validation failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/system/status",
  "code": "validation_failed",
  "traceId": "01J00000000000000000000000",
  "errors": {
    "echo": [
      "The field echo must be between 1 and 64 characters."
    ]
  }
}
```

Начальный набор стабильных кодов:

- `invalid_request`;
- `validation_failed`;
- `unauthorized`;
- `forbidden`;
- `not_found`;
- `method_not_allowed`;
- `internal_error`.

`type` всегда равен `urn:template:problem:{code}`. `errors` присутствует только
для field-level validation; каждый segment dotted property path преобразуется
в camelCase, а сообщения коллизий после нормализации объединяются без потерь.
UI локализует ошибку по `code`, а не по invariant-English тексту сообщений.

Stack traces, SQL, exception messages, secrets и другие внутренние детали клиенту не возвращаются. Unhandled exception логируется с trace ID и преобразуется в безопасный `500 internal_error`. Неизвестный `/api/**` route получает `404 not_found`, а известный route с неподдерживаемым методом — `405 method_not_allowed`.

Health `503` является осознанным исключением: endpoint возвращает typed health envelope со статусом `unhealthy`, а не Problem Details, потому что probe успешно описывает нездоровое состояние приложения.

### 6.5 Validation boundary

`AddValidation` включает built-in Minimal API validation. Data Annotations и HTTP binding metadata применяются только к request DTO и endpoint parameters в `Template.Api`. Они не становятся domain rules и не переносятся в Application.

Validation выполняется до handler. Неавторизованный вызов protected endpoint сначала получает `401`, чтобы validation details не раскрывали защищённый контракт.

Pagination, filtering, транзакции и schema changes неприменимы: iteration 1 не читает и не изменяет коллекции или persistent data.

## 7. Authentication и authorization

Iteration 1 регистрирует настоящий cookie handler, но не реализует пользователя или выдачу cookie:

- scheme name фиксируется централизованно;
- cookie name: `__Host-template.session`;
- `HttpOnly`;
- `Secure`;
- `SameSite=Lax`;
- `Path=/`;
- `Domain` отсутствует;
- API challenge и forbid возвращают `401` и `403`, а не HTML redirect.

CORS не включается, потому что целевая topology строго same-origin. Browser bearer tokens и browser storage для credentials запрещены.

Versioned route group требует authenticated user по умолчанию. Начальная именованная policy требует только authenticated principal; product roles/permissions не проектируются заранее. Integration fixture подменяет default authentication scheme на test-only handler. Test-only policy/fault endpoints не существуют в production и не входят в OpenAPI.

Antiforgery добавляется в iteration 3 до первого state-changing endpoint, доступного по session cookie. В iteration 1 все demonstration endpoints являются безопасными `GET`.

## 8. Future session projection для Next.js

HttpOnly cookie никогда не читается JavaScript. В iteration 3 frontend будет определять authentication state через:

```http
GET /api/v1/auth/session
```

Anonymous response:

```json
{
  "data": {
    "status": "anonymous"
  }
}
```

Authenticated response:

```json
{
  "data": {
    "status": "authenticated",
    "user": {
      "id": "user-id",
      "name": "User name",
      "email": "user@example.com",
      "image": null
    },
    "expiresAt": "2026-07-24T12:00:00Z"
  }
}
```

Оба состояния возвращают `200`, что позволяет UI использовать discriminated union, а не трактовать anonymous state как transport error. Ответ имеет `Cache-Control: no-store`. Browser автоматически отправляет same-origin cookie. При SSR Next.js server explicitly forwards входящий `Cookie` header в ASP.NET Core; API credentials не сохраняются и не преобразуются в bearer token.

Этот endpoint только резервируется документацией iteration 1. Его реализация, точный user DTO, expiry semantics и session refresh принадлежат iteration 3.

## 9. Health

- `/api/health/live` запускает predicate, исключающий dependency checks. Если pipeline способен ответить, процесс считается live.
- `/api/health/ready` запускает только checks с тегом `ready`.
- `/api/health` использует тот же readiness predicate для совместимости с iteration 0, reference E2E readiness URL и будущим UI smoke.
- В iteration 1 dependency checks отсутствуют, поэтому все три endpoints healthy.
- Future database/cache checks должны явно получить тег `ready`; они не входят в liveness.
- Responses содержат только `status` и UTC `timestamp`, без имён внутренних dependencies.
- Healthy возвращает `200`, unhealthy — `503`.
- Все health responses получают `Cache-Control: no-store`.

Пример:

```json
{
  "data": {
    "status": "healthy",
    "timestamp": "2026-07-23T12:00:00Z"
  }
}
```

## 10. Correlation и structured logging

API принимает `X-Correlation-ID`, если значение:

- содержит не более 64 символов;
- использует только ASCII letters, digits, `.`, `_` и `-`.

Некорректное значение игнорируется, чтобы observability metadata не ломала корректный business request. При отсутствии допустимого значения используется текущий framework trace identifier.

Одно каноническое значение возвращается в `X-Correlation-ID`, записывается в `ProblemDetails.traceId` и добавляется в logging scope как `TraceId`.

Request completion log содержит:

- HTTP method;
- request path;
- response status;
- elapsed milliseconds;
- trace ID.

Query values, bodies, cookies, authorization headers и другие credentials не логируются. Health request completion пишется на уровне `Debug`, остальные API requests — `Information` или выше согласно результату. Development использует читаемый console formatter со scopes, production — JSON console со scopes. Дополнительный logging framework не добавляется.

Error/status middleware применяется только к `/api/**`, чтобы будущие Next.js/YARP responses не превращались в API Problem Details.

## 11. OpenAPI и contract discipline

- Используется first-party `Microsoft.AspNetCore.OpenApi`.
- Build-time generation предоставляет `Microsoft.Extensions.ApiDescription.Server`.
- Документ называется `v1` и сериализуется как OpenAPI 3.1.
- Runtime endpoint `/api/openapi/v1.json` существует только в Development и Test.
- Production не публикует dynamic OpenAPI endpoint.
- Cookie scheme описывается как cookie `apiKey` с именем `__Host-template.session`.
- Protected operations имеют security requirement; anonymous operations — нет.
- Operations явно описывают success envelope, validation Problem Details, `401`, `403`, `404`, `405`, `500` и health `200/503`.
- Swagger UI и Scalar не добавляются.
- Canonical artifact хранится в `contracts/openapi/v1.json`.

Dedicated export command генерирует deterministic contract. Integration test семантически сравнивает freshly generated document с committed artifact, поэтому `dotnet test` является contract drift gate для любого CI. Vendor-specific workflow не требуется в iteration 1. Команды export и verification документируются под `/docs`.

## 12. Request flow

```text
/api request
  → correlation scope and response header
  → exception handler
  → status-code Problem Details
  → structured request completion logging
  → cookie authentication
  → authorization
  → built-in endpoint validation
  → endpoint module
  → typed { data } response or Problem Details
```

Порядок гарантирует, что authentication failures, validation failures, missing routes и unhandled exceptions получают одинаковый trace ID и единый формат ошибок.

## 13. Test-first стратегия

`Template.Api.Tests` получает общую `WebApplicationFactory` fixture. Перед каждым блоком реализации сначала добавляется падающий integration test.

Обязательные сценарии:

1. `/api/health`, `/live` и `/ready` возвращают ожидаемый status, envelope и no-store headers.
2. Public system status возвращает `200` и `{ data }`.
3. Invalid `echo` возвращает `400 validation_failed` и field errors.
4. Anonymous protected request возвращает `401 unauthorized`.
5. Test-authenticated request возвращает `200`.
6. Test-only policy endpoint возвращает `403 forbidden`.
7. Unknown `/api/**` route возвращает `404 not_found`.
8. Unsupported method возвращает `405 method_not_allowed`.
9. Test-injected exception возвращает safe `500 internal_error`.
10. Response correlation header совпадает с Problem Details trace ID.
11. Captured request log содержит trace scope и не содержит sensitive input.
12. OpenAPI содержит ожидаемые paths, schemas и cookie security requirements.
13. Generated OpenAPI семантически совпадает с `contracts/openapi/v1.json`.

Test authentication, policy probes и fault injection существуют только в test host. Они не добавляют production endpoints.

Финальная проверка:

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
```

Также запускаются documented OpenAPI export/drift commands и проверяется:

```bash
git diff -- template/
```

UI build и Playwright E2E неприменимы, потому что `apps/web` остаётся пустым. Это фиксируется в acceptance evidence как сознательное `not applicable`, а не как пропущенная проверка.

## 14. Документация и журнал

Implementation change создаёт или обновляет:

- `docs/api-conventions.md` — долговременные REST, error, auth/session, health, observability и OpenAPI conventions;
- `docs/aspnetcore-migration-plan.md` — current iteration, уточнённый scope, mapping, состояние и фактическое acceptance evidence;
- `AGENTS.md` — правило фиксировать forward-looking decisions в подходящем файле под `docs/` в том же change.

Долговременные решения не должны оставаться только в чате, commit message или PR discussion.

## 15. Acceptance criteria

Итерация завершена только если:

1. Все новые файлы находятся вне `template/`.
2. API response/error/versioning/auth conventions реализованы централизованно.
3. Public, protected, validation и failure paths проходят integration tests.
4. Health alias/live/ready работают и имеют documented semantics.
5. OpenAPI экспортирован, валиден и совпадает с generated document.
6. Cookie handler не содержит Identity, user store или login flow.
7. Domain/Application/Infrastructure не содержат HTTP foundation concerns.
8. Restore, build, tests и contract checks проходят.
9. `git diff -- template/` пуст.
10. Migration register содержит команды, результаты и известные расхождения.

## 16. Известные расхождения и риски

Расхождения с reference:

- ошибки переходят с nested error envelope на RFC Problem Details;
- health success переходит под `{ data }`;
- появляются отдельные live/ready probes;
- добавляются технические `/api/v1/system/**` endpoints;
- OpenAPI и correlation ID являются новой возможностью;
- UI и browser E2E отсутствуют до iteration 2.

Риски и решения:

- `__Host-` cookie требует HTTPS. Это не блокирует iteration 1, потому что cookie ещё не выдаётся; local HTTPS/proxy topology фиксируется до реализации auth flow в iteration 3.
- Future UI/YARP responses не должны получать API Problem Details, поэтому error middleware ограничен `/api/**`.
- Session projection всегда `no-store` и не выдаёт browser-readable credentials.
- Любое breaking изменение `/api/v1` требует явного versioning/deprecation решения.
- OpenAPI artifact должен быть deterministic; semantic comparison является обязательным acceptance gate.

Dependency gate iteration 0 выполнен. Блокеров для implementation plan нет.
