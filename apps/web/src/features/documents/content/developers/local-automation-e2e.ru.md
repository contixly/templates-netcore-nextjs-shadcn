---
title: "Локальная автоматизация и E2E"
description: "Использование локальных endpoints ASP.NET Core и helpers сгенерированного SDK в детерминированных сценариях Playwright."
group: "Для разработчиков"
groupOrder: 300
parentItem: "Процесс качества"
parentItemOrder: 70
order: 20
toc: true
purpose: "Инструкция по тестированию для разработчиков"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Локальная автоматизация и E2E

ASP.NET Core предоставляет намеренно локальный credential flow для Playwright и браузерной
проверки. Он создает реальных пользователей в чистом store и постоянные сессии в HttpOnly cookies,
не добавляя упрощенный production-вход.

## Граница доступности

Локальная автоматизация доступна, только когда выполняются оба условия:

- окружение API — `Development` или `Test`;
- задано `LocalAutomationAuth__Enabled=true`.

Production возвращает `404 local_auth_disabled`, даже если флаг случайно включен. Не помещайте
реальные учетные данные в зафиксированные настройки: локальный flow генерирует собственные данные
сценария. Каждый небезопасный automation request следует обычному CSRF-контракту.

## Оркестрация Playwright

`npm run e2e` использует `apps/web/playwright.config.ts`. Он запускает
`apps/api/tests/Template.E2EHost`, который создает и мигрирует одноразовую PostgreSQL 18.4 базу и
запускает настоящий процесс `Template.Api`. Затем Next.js запускается на `127.0.0.1:3127` с локальным
rewrite `/api/**` к API на `127.0.0.1:5297`.

`Template.E2EHost` — оркестратор, а не второй HTTP host. Readiness probe использует
`/api/health/ready`; обычное завершение останавливает Next.js, процесс API и одноразовую базу.

## Создание, вход и очистка

Используйте helpers сгенерированного SDK из
`apps/web/e2e/support/generated-auth-api.ts`:

- `createLocalAutomationUser` создает сценарий через `/api/local-auth/scenario`;
- `signInLocalAutomationUser` создает еще одну постоянную сессию, когда она нужна сценарию;
- `confirmGeneratedLocalAutomationEmail` подтверждает email только подходящего локального
  пользователя сценария;
- `cleanupLocalAutomationUser` удаляет текущий локальный сценарий через аутентифицированный контекст.

Helpers получают свежий CSRF token и используют same-origin сгенерированный client. Изолируйте
browser context каждого пользователя. Регистрируйте cleanup сразу после создания сценария и никогда
не печатайте возвращенные passwords, API keys, cookies или тела ответов с секретами.

## Пишите точечный сценарий

Предпочитайте role-based locators и ожидайте явных markers готовности приложения к взаимодействию.
Проверяйте поведение браузера и наблюдаемый REST request, а не внутреннее состояние компонента или
прямой SQL. Используйте API helpers для подготовки только поддерживаемого контрактом поведения. Не
изменяйте флаги подтверждения напрямую в PostgreSQL.

Во время работы запускайте один файл, затем полный детерминированный suite:

```bash
cd apps/web
npm run e2e -- authentication.spec.ts
npm run e2e
```

Навигация к реальным OAuth providers отделена и включается явно через
`E2E_LIVE_PROVIDER_SMOKE=1`; она не отправляет учетные данные и не доказывает callback.

## Связанные страницы

- [Требования, E2E и документация](/docs/developers/openspec-e2e-docs)
- [Безопасность runtime](/docs/application/runtime-security)
- [Настройки аккаунта](/docs/account)
- [Рабочее пространство](/docs/workspace)
