# Bootstrap-итерация репозитория: план выполнения

> Каноническая долгосрочная дорожная карта находится в [`../../aspnetcore-migration-plan.md`](../../aspnetcore-migration-plan.md). Этот документ описывает только итерацию 0 и не является планом полного переноса за один проход.

## Цель

Подготовить безопасный новый каркас для будущей миграции, сохранив исходный Next.js template неизменяемым в `template/`.

## Шаги

- [x] 1. Создать ветку `codex/chore/bootstrap-aspnetcore-migration`.
- [x] 2. Переместить все отслеживаемые файлы прежнего репозитория в `template/` через Git rename; `.git/` не перемещать.
- [x] 3. Инициализировать новый пустой root OpenSpec без активных changes/specs.
- [x] 4. Создать корневые каталоги `apps/api`, `apps/web`, `contracts/openapi`, `deploy`, `orchestration` и `docs`.
- [x] 5. Добавить root .NET conventions и создать корневой `Template.sln` с чистыми слоями Domain, Application, Infrastructure, Api и Api.Tests.
- [x] 6. Сначала добавить тест `GET /api/health`, убедиться в его red-состоянии, затем реализовать минимальный endpoint и вернуть тест в green.
- [x] 7. Добавить новый корневой `AGENTS.md` (и symlink `CLAUDE.md`) с запретом правок `template/` и актуальными командами.
- [x] 8. Проверить solution, API test, OpenSpec initialization, root tree и отсутствие изменений внутри `template/` после исходного переноса.

## Acceptance criteria

- `template/` содержит прежний репозиторий, а новый root не содержит его runnable Next.js sources.
- `Template.sln` в корне собирается и содержит пять проектов.
- `GET /api/health` возвращает HTTP 200 в integration test.
- `docs/aspnetcore-migration-plan.md` содержит подробный итерационный план последующей работы.
- В `openspec/changes/` нет активной спецификации.
