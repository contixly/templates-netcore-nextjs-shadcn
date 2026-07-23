# Repository Instructions

## Reference and migration process

- `template/` is the immutable reference application. Read it to reproduce behavior, but never edit, move, delete, format, or run migrations inside it.
- Start any future migration slice with [`docs/aspnetcore-migration-plan.md`](docs/aspnetcore-migration-plan.md), then update its iteration register and acceptance evidence.
- This repository starts from a clean database and identity store. Do not design data or session migration from `template/`.
- OpenSpec is initialized only. Do not create an active OpenSpec change/spec unless the user explicitly asks for it.

## Target layout

- `apps/api/` — .NET 10 solution; `Template.Api` is the only HTTP host.
- `apps/api/src/Template.Domain` has no infrastructure or HTTP dependencies.
- `Template.Application` depends only on Domain; `Template.Infrastructure` implements application/domain ports; `Template.Api` depends on Application and Infrastructure.
- `apps/web/` is the future Next.js UI. It calls the API over REST and must not contain Prisma, Better Auth, Server Actions, or direct database access.
- `contracts/openapi/` stores exported OpenAPI contracts and client-generation configuration. `deploy/` and `orchestration/` are reserved for later production and Aspire work.

## API and security conventions

- ASP.NET Core owns `/api/**`; production will proxy all other paths to Next.js on the same origin.
- Keep browser sessions in secure HttpOnly cookies; never store bearer tokens in browser storage.
- Validate and authorize at the HTTP boundary; keep business rules in Application/Domain.
- Add database, Identity, OAuth, API keys, YARP, Aspire, or product endpoints only in their own planned iteration.

## Development workflow

- Write a failing test before implementing behavior, then run the focused test and the full solution test suite.
- Keep test projects under `apps/api/tests/`; use `WebApplicationFactory` for API endpoint tests.
- Run `dotnet restore Template.sln`, `dotnet build Template.sln --no-restore`, and `dotnet test Template.sln --no-restore` before completing .NET work.
- Confirm that `git diff -- template/` contains no post-relocation edits before completing a migration task.
- When `apps/web` exists, read the installed Next.js documentation before changing Next.js code.
