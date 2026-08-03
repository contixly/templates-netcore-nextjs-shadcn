---
title: "Безопасность среды выполнения"
description: "Настройки среды выполнения, важные для безопасности: базовый адрес приложения, хосты изображений, браузерные заголовки и защищенные маршруты."
group: "Приложение"
groupOrder: 500
parentItem: "Среда выполнения"
parentItemOrder: 90
order: 10
toc: true
purpose: "Справочник по безопасности среды выполнения"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Безопасность среды выполнения

Безопасность runtime разделена между HTTP-границей ASP.NET Core и тонким UI Next.js. Same-origin схема не отдает учетные данные браузерному JavaScript, а API остается авторитетным.

## Сессия и CSRF

`__Host-template.session` имеет `HttpOnly`, `Secure`, `SameSite=Lax`, path `/`, без `Domain` и постоянный семидневный sliding expiration. Она содержит защищенный Data Protection непрозрачный ключ ticket store. PostgreSQL хранит его SHA-256 hash и отдельно защищенный ticket.

Каждая небезопасная браузерная операция вызывает `GET /api/v1/auth/csrf` и отправляет `X-CSRF-TOKEN` с парной strict antiforgery cookie. Challenge/forbid API возвращает JSON `401`/`403`, а не HTML redirects. JavaScript не читает cookies и не хранит bearer token.

## Protection и OAuth state

Ключи Data Protection сохраняются в PostgreSQL с discriminator `Template`. В Production также нужен RSA PFX из `DataProtection__CertificatePath` и `DataProtection__CertificatePassword`; invalid material останавливает startup. State OpenIddict Client защищен и одноразовый; provider tokens существуют только в callback и не сохраняются.

## Origins и маршрутизация

Браузер вызывает относительные same-origin `/api/**`. SSR использует server-only `API_INTERNAL_BASE_URL` и передает только разрешенные cookie/correlation данные. `API_PROXY_TARGET` — rewrite для Development/E2E; будущая production topology отдает `/api/**` Kestrel. CORS не включен.

`APP_PUBLIC_ORIGIN` настраивает metadata URL Next.js. OAuth отдельно проверяет `ExternalAuthentication__PublicOrigin`, требуя HTTPS кроме loopback разработки.

Ответы API/auth/health/account/collaboration/search/API keys с состоянием используют `Cache-Control: no-store`. Наружу выходит безопасный correlation `traceId`, но не stack traces, SQL, секреты, cookies или authorization headers.

Production proxy YARP/Kestrel, container hardening, финальная оболочка и Redis/Aspire orchestration остаются будущими итерациями.

## Связанные страницы

- [OAuth-провайдеры](/docs/application/oauth-providers)
- [Кеширование](/docs/application/caching)
- [API-доступ](/docs/api)
- [Сессии и безопасность](/docs/account/sessions-security)
