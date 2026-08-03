---
title: "Архитектура функциональных срезов"
description: "Размещение функции в Domain, Application, Infrastructure, Api и отдельном UI Next.js с сохранением зависимостей внутрь."
group: "Для разработчиков"
groupOrder: 300
parentItem: "Развитие проекта"
parentItemOrder: 100
order: 20
toc: true
purpose: "Инструкция для разработчиков"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Архитектура функциональных срезов

Функциональный срез проходит через backend-слои одной продуктовой возможности и, когда нужно,
web-UI. Это не означает объединение слоев в одну папку или перенос бизнес-правил во внешний слой.

## Размещение на backend

| Слой                      | Типичное содержимое функции                                                          | Допустимые зависимости                         |
| ------------------------- | ------------------------------------------------------------------------------------ | ---------------------------------------------- |
| `Template.Domain`         | Value objects, закрытые политики и правила без I/O.                                  | Ничего из Application, Infrastructure или Api. |
| `Template.Application`    | Сценарии использования, application models и порты в `Ports/`.                       | Только Domain.                                 |
| `Template.Infrastructure` | EF Core stores, адаптеры Identity/OpenIddict, криптография и реализации портов.      | Application и Domain.                          |
| `Template.Api`            | Контракты запросов/ответов, граничные helpers, `IEndpointModule` и OpenAPI metadata. | Application и композиция Infrastructure.       |

Ориентируйтесь на существующие папки возможностей, например `Organizations`, `Collaboration` или
`ApiKeys`. `Template.Api` — единственный HTTP host. Он валидирует и авторизует запрос на границе,
затем делегирует в Application. Domain и Application не должны знать о `HttpContext`, результатах
Minimal API, EF Core или React.

## Размещение в web

Отдельный UI использует такие границы:

- маршруты и layouts в `apps/web/src/app` собирают страницы;
- продуктовая координация находится в `apps/web/src/features`;
- переиспользуемые визуальные controls находятся в `apps/web/src/components`;
- серверные и браузерные API adapters находятся в `apps/web/src/lib/api`;
- transport DTOs и операции берутся из `apps/web/src/lib/api/generated`.

Next.js не импортирует .NET assemblies, не обращается к PostgreSQL или хранилищу аутентификации и
не владеет маршрутами `/api/**`. Для серверного чтения и браузерных действий он использует REST.

## Добавляйте поведение через тесты

1. Добавьте падающий unit-тест Domain или Application для бизнес-правила.
2. Добавьте падающий API-тест через `WebApplicationFactory` для HTTP-валидации, авторизации,
   статуса, заголовков и формы ответа.
3. Реализуйте минимальное изменение внутренних слоев и тонкий endpoint Minimal API.
4. Экспортируйте `contracts/openapi/v1.json` и перегенерируйте TypeScript SDK.
5. Добавьте или обновите адаптер Next.js и его точечный Jest-тест.
6. Добавляйте Playwright только для полного браузерного сценария, которому это действительно нужно.

Порты принадлежат Application, а их внешние реализации — Infrastructure. React-компонент не должен
повторять правило авторизации из Application или Domain: клиентская валидация лишь улучшает UX, API
остается авторитетным.

## Зависимости между срезами

Не позволяйте одной функции обращаться к внутреннему store или endpoint boundary другой функции.
Обобщайте доменное понятие, порт или сценарий Application либо явный API-контракт. Делайте изменение
достаточно небольшим, чтобы тесты владельца и публичная документация однозначно показывали
возможность.

## Связанные страницы

- [REST-граница вместо Server Actions](/docs/developers/server-actions)
- [Добавить endpoint API v1](/docs/developers/api-v1-endpoint)
- [Оболочка приложения](/docs/application)
- [Как писать документацию](/docs/general/authoring/how-to-write-docs)
