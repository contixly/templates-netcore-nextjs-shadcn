---
title: "Оболочка приложения"
description: "Публичные точки входа, защищенная оболочка приложения, стартовая панель и точки расширения функций."
group: "Приложение"
groupOrder: 500
parentItem: "Основа"
parentItemOrder: 100
order: 10
toc: true
purpose: "Обзор приложения"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Оболочка приложения

Система — REST-приложение со строгой границей: ASP.NET Core владеет `/api/**`, аутентификацией, авторизацией, бизнес-сценариями, persistence и OAuth; Next.js владеет рендерингом и браузерным взаимодействием.

## Поток данных

Серверные и браузерные функции используют зафиксированный OpenAPI-контракт и generated REST SDK. Адаптеры извлекают `{ data }` и преобразуют Problem Details в безопасные ошибки UI. Они не используют raw `fetch`, не переопределяют DTO, не обращаются к базе или auth storage. Для продуктовых данных нет Server Actions или Next.js API Route Handlers.

Браузер вызывает same-origin `/api/**` с автоматическими cookies. SSR создает изолированный клиент для каждого контекста, использует server-only `API_INTERNAL_BASE_URL` и передает только разрешенные cookie/correlation данные. Bearer tokens не хранятся.

## Слои backend

- `Template.Domain` содержит domain value objects и правила без зависимостей от HTTP или Infrastructure.
- `Template.Application` содержит use cases и ports и зависит только от Domain.
- `Template.Infrastructure` реализует persistence, Identity, OpenIddict Client, криптографию и другие ports.
- `Template.Api` — единственный HTTP-хост; он валидирует и аутентифицирует на границе до Application.

Бизнес-правила находятся в Application или Domain, а не в endpoint handlers или React-компонентах.

## Текущий и будущий UI

Публичный UI включает `/`, `/auth/login`, `/auth/error` и `/docs/**`. Защищенные поверхности: временный resolver `/dashboard`, `/welcome`, `/workspaces`, `/user/**`, `/invite/{invitationId}` и `/w/{organizationKey}/**`. Текущие страницы обслуживают итерации 1–7. Финальные dashboard, навигация и визуальная оболочка отложены до итерации 9.

## Связанные страницы

- [Быстрый старт](/docs/general/quick-start)
- [Рабочее пространство](/docs/workspace)
- [Локализация](/docs/application/localization)
- [Безопасность среды выполнения](/docs/application/runtime-security)
- [Архитектура функциональных срезов](/docs/developers/feature-slice)
