---
title: "Кеширование"
description: "Текущая политика no-store и Cache Components; Redis и Valkey отложены до итерации 10."
group: "Приложение"
groupOrder: 500
parentItem: "Среда выполнения"
parentItemOrder: 90
order: 20
toc: true
purpose: "Справочник по кешированию"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Кеширование

Текущее приложение предпочитает корректность для зависимых от сессии и изменяемых REST-данных. Cache Components включены для рендеринга, но аутентифицированные API-проекции не становятся общим кешем приложения.

## Текущие правила no-store

Серверные API-клиенты используют `cache: "no-store"`. Границы auth, session, account, organization, collaboration, document search, health и API keys используют `Cache-Control: no-store` там, где это требует изменяемый/request-specific контракт. Мутации отображают подтвержденные результаты API, а не инвалидируют кеш базы Next.js.

Cookie-bearing SSR подавляет sliding renewal, потому что Server Component не может передать API `Set-Cookie`. За renewal отвечает один обычный same-origin браузерный запрос, чтобы защищенная cookie с `HttpOnly` попала в cookie jar.

## Граница Cache Components

Runtime SSR начинается ниже `connection()` и `Suspense`, когда доступны request headers и runtime configuration. Build не требует живого API, а cookies и `API_INTERNAL_BASE_URL` не фиксируются в cached output. Статический presentation-контент и документация могут использовать framework caches без кеширования приватных REST-ответов.

## Не реализовано

Сейчас нет Redis/Valkey handler, remote cache configuration, repository cache tags или cross-instance invalidation contract. Redis/Aspire orchestration относится к итерации 10. Distributed caching добавляется только отдельным архитектурным решением с ownership, tenant-safe keys, invalidation и тестами.

## Связанные страницы

- [Оболочка приложения](/docs/application)
- [Безопасность среды выполнения](/docs/application/runtime-security)
- [Быстрый старт](/docs/general/quick-start)
