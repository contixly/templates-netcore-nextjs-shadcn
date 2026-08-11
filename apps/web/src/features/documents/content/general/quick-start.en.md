---
title: "Quick start"
description: "Configure PostgreSQL, start the ASP.NET Core API and separate Next.js UI, and verify the local REST boundary."
group: "General"
groupOrder: 2000
parentItem: "Getting started"
parentItemOrder: 900
order: 10
toc: true
purpose: "Template setup tutorial"
status: "published"
author: "Template Maintainers"
version: "1.3.0"
editedAt: "2026-08-11"
---

# Quick start

This tutorial starts the current two-application template locally: `Template.Api` is the only HTTP
API host, and `apps/web` is a separate Next.js UI that calls it over REST. The repository assumes a
clean PostgreSQL database and identity store; it does not migrate data from the legacy reference.

## What you need

- the .NET 10 SDK selected by `global.json`;
- Node.js 22.18 or newer and the npm version recorded by `apps/web/package.json`;
- a clean PostgreSQL database;
- Docker when running Testcontainers-based integration tests or Playwright E2E.

OAuth credentials are optional. A provider appears only when its complete local configuration is
present.

## Restore dependencies

From the repository root:

```bash
dotnet tool restore
dotnet restore Template.sln
cd apps/web
npm ci
cd ../..
```

Do not install Prisma or Better Auth. ASP.NET Core and EF Core own persistence and identity; Next.js
uses the committed generated REST SDK.

## Configure the API

Set the PostgreSQL connection string outside tracked files:

```bash
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=template;Username=postgres;Password=postgres'
```

The HTTPS launcher prefers this environment value. Alternatively, add
`ConnectionStrings:Postgres` to the ignored `apps/api/src/Template.Api/appsettings.Local.json` with
file mode `0600`; never add the real value to a tracked appsettings file.

For optional OAuth configuration, copy the shape from
`apps/api/src/Template.Api/appsettings.Local.example.json` into the ignored
`appsettings.Local.json`, replace only the providers you need, and keep the real file mode `0600`.
Never commit credentials.

Apply migrations explicitly; the API never applies them at startup:

```bash
dotnet ef database update \
  --project apps/api/src/Template.Infrastructure/Template.Infrastructure.csproj \
  --startup-project apps/api/src/Template.Api/Template.Api.csproj \
  --context TemplateDbContext
```

## Start with local HTTPS

The recommended one-command development topology uses the same HTTPS origin for the browser and
all `/api/**` callbacks:

```bash
./scripts/run-local-https.sh
```

The launcher trusts and exports the .NET development certificate, validates or restores the
Next.js installation against `package-lock.json`, applies EF Core migrations, enables the
Development-only local-automation sign-in boundary, and starts both applications. It forces the
external-authentication public origin to `https://localhost:3000`, so the HTTP value in the shared
local example remains compatible with the manual launch profile below. Register the HTTPS provider
callback URLs documented in the authentication operations guide. Press `Ctrl+C` to stop both
process groups and remove the temporary exported certificate.

Open `https://localhost:3000`, or use `https://localhost:3000/auth/login` to create a local session.
The API listens at `https://localhost:7297` and remains behind the same-origin Next.js proxy for
browser calls.

The remaining sections describe the alternative two-terminal HTTP workflow.

## Configure the web UI for manual HTTP launch

Create the ignored local environment file:

```bash
cp apps/web/.env.example apps/web/.env.local
```

The example points `API_INTERNAL_BASE_URL` and the development-only `API_PROXY_TARGET` to
`http://127.0.0.1:5297`. `PUBLIC_DEFAULT_LOCALE` accepts `en` or `ru`. Do not add a public API origin
or a browser token: browser calls stay same-origin and use secure HttpOnly cookies.

## Start both applications manually

In the first terminal, keep `ConnectionStrings__Postgres` set and start the API:

```bash
dotnet run --project apps/api/src/Template.Api/Template.Api.csproj
```

The development launch profile listens on `http://localhost:5297` and enables the local-automation
sign-in boundary. In a second terminal:

```bash
cd apps/web
npm run dev
```

Open `http://localhost:3000`. Use `/docs` for public documentation or `/auth/login` and **Create
local automation user** for a generated local session. The UI sends `/api/**` through the local
rewrite; ASP.NET Core remains the owner of those routes.

## Verify the setup

Check API liveness/readiness, the web home page, login, and documentation. Before product work, run
the core gates:

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

`dotnet test` and `npm run e2e` require Docker because their orchestration creates disposable
PostgreSQL databases. Run `npm run e2e` when you are ready to verify the full browser workflow.

## Next steps

- Read [Application shell](/docs/application) for the API/UI ownership boundary.
- Read [Workspace](/docs/workspace) for organization-backed collaboration.
- Read [API access](/docs/api) before creating machine credentials.
- Read [For developers](/docs/developers) before adding a feature.
