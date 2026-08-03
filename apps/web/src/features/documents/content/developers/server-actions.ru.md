---
title: "Серверные действия"
description: "Устаревшее руководство по Server Actions и целевая REST-граница между API ASP.NET Core и отдельным UI Next.js."
group: "Для разработчиков"
groupOrder: 300
parentItem: "Развитие проекта"
parentItemOrder: 100
order: 30
toc: true
purpose: "Инструкция для разработчиков"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Серверные действия

Этот канонический маршрут сохранен, чтобы существующие ссылки продолжали работать. Прежний
full-stack паттерн Next.js теперь считается legacy guidance: целевое приложение не использует
Server Actions для продуктового чтения или мутаций. ASP.NET Core владеет всеми операциями
`/api/**`, а отдельный UI Next.js общается с ним через REST.

## Почему граница изменилась

Единая REST-граница дает браузерному коду, серверному рендерингу, автоматическим тестам и внешним
потребителям один наблюдаемый контракт. ASP.NET Core остается авторитетом для валидации,
аутентификации, авторизации, бизнес-сценариев, persistence, rate limits и Problem Details. OpenAPI
фиксирует этот контракт, а сгенерированный TypeScript SDK синхронизирует с ним UI.

Мутация в Server Action создала бы вторую серверную границу приложения, обошла зафиксированный
OpenAPI-контракт и могла бы продублировать правила авторизации или доступа к данным. Поэтому в
Next.js также нет Prisma, Better Auth, прямого доступа к базе и продуктовых API Route Handlers.

## Читайте через REST

Server Components создают изолированный клиент сгенерированного SDK с серверным
`API_INTERNAL_BASE_URL`. Loader передает только разрешенный контекст cookie и correlation. Безопасная
проекция с cookie может добавить узкий marker подавления продления сессии, потому что Server
Component не может доставить браузеру renewal cookie из ответа API.

Client Components используют относительный same-origin client с `credentials: "same-origin"`. При
локальной разработке `API_PROXY_TARGET` включает rewrite Next.js к ASP.NET Core; в итоговой
same-origin topology ASP.NET Core напрямую владеет `/api/**`.

## Изменяйте через REST

Для браузерной мутации:

1. получите свежий request token из `GET /api/v1/auth/csrf`;
2. вызовите операцию сгенерированного SDK с `X-CSRF-TOKEN`;
3. считайте ответ API авторитетным;
4. преобразуйте Problem Details в безопасный UI result;
5. обновите или согласуйте состояние сгенерированными GET без повтора уже выполненной мутации.

Защищенная HttpOnly session cookie передается автоматически. JavaScript никогда не читает ее и не
сохраняет bearer token. Машинные интеграции используют документированный контракт `x-api-key`, а
не сценарий браузерной сессии.

## Перенос legacy action

Напишите падающий тест Application/API для поведения, перенесите бизнес-правила в Domain или
Application, скройте persistence за портом Application и откройте тонкую операцию Minimal API в
`Template.Api`. Экспортируйте OpenAPI, перегенерируйте SDK, затем замените вызов Server Action на
серверный или браузерный REST adapter. Удалите legacy transport types и прямые database imports
вместо поддержки двух путей.

## Связанные страницы

- [Архитектура функциональных срезов](/docs/developers/feature-slice)
- [Добавить endpoint API v1](/docs/developers/api-v1-endpoint)
- [Безопасность runtime](/docs/application/runtime-security)
- [Справочник API v1](/docs/api/api-v1)
