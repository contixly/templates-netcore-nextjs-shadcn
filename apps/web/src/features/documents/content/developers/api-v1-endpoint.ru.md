---
title: "Добавить маршрут API v1"
description: "Добавление endpoint Minimal API на ASP.NET Core через тесты, публикация OpenAPI-контракта и использование сгенерированного REST SDK."
group: "Для разработчиков"
groupOrder: 300
parentItem: "Разработка API"
parentItemOrder: 80
order: 10
toc: true
purpose: "Инструкция для разработчиков"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Добавить маршрут API v1

Используйте этот процесс для новой операции в поверхности ASP.NET Core `/api/v1`. Сначала решите,
предназначена ли операция для браузерной сессии, API-ключа, обоих типов учетных данных или анонимного
доступа. Это решение выбирает группу endpoint и security contract; не создавайте Route Handler в
Next.js.

## Начните с падающих тестов

1. Добавьте тест Application в `apps/api/tests/Template.Application.Tests` для сценария или
   доменного правила.
2. Добавьте API-тест в `apps/api/tests/Template.Api.Tests` через общий host
   `ApiWebApplicationFactory`/`WebApplicationFactory`.
3. Проверьте наблюдаемый контракт: метод и путь, режим учетных данных, валидацию, авторизацию,
   success envelope, Problem Details, заголовки и безопасные ошибки.
4. Запустите точечный тест и сохраните его падение как RED-свидетельство до реализации.

Если маршрут меняет scope API-ключа или tenant-поведение, добавьте покрытие соответствующих прав,
principal, изоляции и rate limit. Не выводите эти правила только из поведения UI.

## Реализуйте срез внутрь

Добавляйте правило или value object в Domain, только если понятие действительно доменное.
Оркестрацию и порты размещайте в `Template.Application`, а внешний I/O реализуйте за этими портами в
`Template.Infrastructure`. Transport contracts и граничное преобразование находятся в
`Template.Api/Features/{Capability}`.

Подключите операцию в `IEndpointModule` своей возможности через подходящую группу
`EndpointRouteContext`:

- `VersionedApi` для операций браузерной сессии;
- `VersionedMixedApi` для явно поддерживаемого чтения с cookie или API-ключом;
- `VersionedMachineApi` для операций только по API-ключу;
- явный `AllowAnonymous()` только для намеренно публичной операции.

Обработчик Minimal API валидирует и авторизует запрос на HTTP-границе, вызывает Application и
преобразует результат. Он не содержит persistence или бизнес-правил.

## Опишите HTTP-контракт

Успешный JSON использует типизированный envelope `{ "data": ... }`. Ошибки используют
`application/problem+json` со стабильным `code` и безопасным `traceId`; validation добавляет
`errors`. Задайте уникальное имя операции и точные metadata `Produces`, чтобы OpenAPI описывал все
ответы и режимы безопасности. Небезопасные браузерные операции также используют существующий
контракт CSRF endpoint/filter.

Если меняется публичное поведение потребителя, обновите [соглашения API](/docs/api/api-v1).

## Экспортируйте OpenAPI и перегенерируйте SDK

Из корня репозитория:

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
```

Проверьте и зафиксируйте `contracts/openapi/v1.json`. Затем выполните в `apps/web`:

```bash
npm run api:generate
npm run api:check
```

`api:generate` заменяет зафиксированное дерево `src/lib/api/generated`. Не редактируйте
сгенерированные файлы вручную. `api:check` повторяет генерацию и сравнивает дерево побайтно.

## Подключите UI Next.js

Вызывайте сгенерированную операцию из специализированного адаптера в `apps/web/src/lib/api`.
Браузерные клиенты используют same-origin credentials; SSR-клиенты — `API_INTERNAL_BASE_URL` и
явно разрешенный передаваемый контекст. Браузерная мутация получает свежий CSRF token. Не
используйте raw `fetch`, handwritten transport DTOs, Server Actions, прямой доступ к базе или
bearer tokens в браузерном хранилище.

В завершение запустите точечные тесты, полный .NET suite, проверки сгенерированного контракта,
релевантные Jest-тесты и сценарий Playwright, только если операция участвует в браузерном процессе.

## Связанные страницы

- [Справочник API v1](/docs/api/api-v1)
- [Архитектура функциональных срезов](/docs/developers/feature-slice)
- [REST-граница вместо Server Actions](/docs/developers/server-actions)
- [Локальная автоматизация и E2E](/docs/developers/local-automation-e2e)
