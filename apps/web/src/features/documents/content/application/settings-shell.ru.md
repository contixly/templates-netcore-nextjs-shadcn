---
title: "Оболочка настроек"
description: "Как страницы настроек аккаунта и рабочего пространства остаются единообразными между разделами и темами."
group: "Приложение"
groupOrder: 500
parentItem: "Настройки"
parentItemOrder: 60
order: 10
toc: true
purpose: "Описание поверхности настроек"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Оболочка настроек

Текущие страницы настроек — работающие REST-поверхности аккаунта, организаций, совместной работы и API-ключей. Это не финальная визуальная оболочка, запланированная на итерацию 9.

## Текущие маршруты

Настройки аккаунта в `/user/**` включают профиль, подключения, безопасность, опасную зону и личные API-ключи. Настройки организации в `/w/{organizationKey}/settings/**` включают сведения, пользователей, роли, команды, приглашения и API-ключи организации.

Slug в URL — presentation context. Серверные loaders получают доверенные сведения и используют канонический UUID для API. Server permissions управляют навигацией и видимостью мутаций; скрытие в UI не является единственной авторизацией.

## Loaders и мутации

Server-rendered loaders используют изолированные клиенты generated REST SDK, `API_INTERNAL_BASE_URL`, `cache: "no-store"` и allow-list cookie/correlation headers. Они не обращаются к PostgreSQL или identity store.

Браузерные мутации используют generated SDK с `credentials: "same-origin"`. Каждый небезопасный вызов получает свежий CSRF и отправляет `X-CSRF-TOKEN`; видимое состояние меняется после подтверждения API. Problem Details преобразуется в безопасный локализованный текст по `code` и опциональному `traceId`.

Не добавляйте raw `fetch`, рукописные transport DTO, Server Actions, Next.js Route Handlers, Prisma, Better Auth, прямой доступ к базе или browser bearer storage.

## Отложенная оболочка

Сегодняшние layouts дают защищенные маршруты, loading/errors, формы, списки, opaque pagination, permission gates и показ секрета для итераций 3–7. Итерация 9 владеет финальными responsive sidebar, dashboard, визуальной системой настроек, темой и parity review по маршрутам.

## Связанные страницы

- [Оболочка приложения](/docs/application)
- [Настройки аккаунта](/docs/account)
- [Настройки рабочего пространства](/docs/workspace/settings)
- [Безопасность среды выполнения](/docs/application/runtime-security)
