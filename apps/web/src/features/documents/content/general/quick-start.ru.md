---
title: "Быстрый старт"
description: "Настройка PostgreSQL, запуск API на ASP.NET Core и отдельного UI Next.js и проверка локальной REST-границы."
group: "Общее"
groupOrder: 2000
parentItem: "Начало работы"
parentItemOrder: 900
order: 10
toc: true
purpose: "Учебная инструкция по запуску шаблона"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Быстрый старт

Эта инструкция локально запускает текущий шаблон из двух приложений: `Template.Api` — единственный
HTTP API host, а `apps/web` — отдельный UI Next.js, который вызывает его через REST. Репозиторий
рассчитан на чистую базу PostgreSQL и identity store; данные из legacy reference не переносятся.

## Что понадобится

- .NET 10 SDK, выбранный в `global.json`;
- Node.js 22.18 или новее и версия npm из `apps/web/package.json`;
- чистая база PostgreSQL;
- Docker для integration tests на Testcontainers или E2E Playwright.

OAuth credentials необязательны. Provider появляется, только когда присутствует его полная
локальная configuration.

## Восстановите зависимости

Из корня репозитория:

```bash
dotnet tool restore
dotnet restore Template.sln
cd apps/web
npm ci
cd ../..
```

Не устанавливайте Prisma или Better Auth. ASP.NET Core и EF Core владеют persistence и identity;
Next.js использует зафиксированный сгенерированный REST SDK.

## Настройте API

Задайте строку подключения PostgreSQL вне отслеживаемых файлов:

```bash
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=template;Username=postgres;Password=postgres'
```

Для необязательного OAuth скопируйте форму из
`apps/api/src/Template.Api/appsettings.Local.example.json` в игнорируемый
`appsettings.Local.json`, замените только нужных providers и задайте реальному файлу mode `0600`.
Никогда не фиксируйте credentials.

Примените migrations явно: API не применяет их при запуске.

```bash
dotnet ef database update \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
```

## Настройте web-UI

Создайте игнорируемый локальный environment file:

```bash
cp apps/web/.env.example apps/web/.env.local
```

Пример направляет `API_INTERNAL_BASE_URL` и используемый только при разработке `API_PROXY_TARGET` на
`http://127.0.0.1:5297`. `PUBLIC_DEFAULT_LOCALE` принимает `en` или `ru`. Не добавляйте публичный
адрес API или browser token: браузерные вызовы остаются same-origin и используют защищенные HttpOnly
cookies.

## Запустите оба приложения

В первом терминале оставьте заданным `ConnectionStrings__Postgres` и запустите API:

```bash
dotnet run --project apps/api/src/Template.Api/Template.Api.csproj
```

Development launch profile слушает `http://localhost:5297` и включает локальную границу входа для
автоматизации. Во втором терминале:

```bash
cd apps/web
npm run dev
```

Откройте `http://localhost:3000`. Перейдите в `/docs` для публичной документации или в
`/auth/login` и нажмите **Create local automation user**, чтобы получить сгенерированную локальную
сессию. UI направляет `/api/**` через локальный rewrite; владельцем этих routes остается ASP.NET
Core.

## Проверьте настройку

Проверьте liveness/readiness API, главную web-страницу, вход и документацию. Перед продуктовой
разработкой запустите основные gates:

```bash
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
cd apps/web
npm run api:check
npm run boundaries:check
npm run content:check
npm run typecheck
npm test -- --runInBand
npm run build
```

`dotnet test` и `npm run e2e` требуют Docker, потому что их оркестрация создает одноразовые базы
PostgreSQL. Выполните `npm run e2e`, когда будете готовы проверить полный браузерный процесс.

## Что дальше

- Прочитайте [Оболочка приложения](/docs/application) о границе ответственности API/UI.
- Прочитайте [Рабочее пространство](/docs/workspace) о совместной работе на базе организаций.
- Прочитайте [API-доступ](/docs/api) перед созданием машинных credentials.
- Прочитайте [Для разработчиков](/docs/developers) перед добавлением функции.
