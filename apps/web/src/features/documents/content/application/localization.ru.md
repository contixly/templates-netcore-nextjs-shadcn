---
title: "Локализация"
description: "Как шаблон использует локаль по умолчанию для UI-сообщений, метаданных и документации."
group: "Приложение"
groupOrder: 500
parentItem: "Локализация"
parentItemOrder: 70
order: 10
toc: true
purpose: "Описание локализации"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Локализация

Локализация относится к presentation-слою Next.js. REST-маршруты ASP.NET Core и поля JSON остаются нейтральными, поэтому язык не меняет API-контракт или URL.

## Фиксированная локаль развертывания

Поддерживаются `en` и `ru`. `PUBLIC_DEFAULT_LOCALE` выбирает одну локаль для всего экземпляра; отсутствующее или неподдерживаемое значение заменяется на `en`. Build и runtime используют одно значение, а смена языка требует rebuild/restart.

У маршрутов нет префикса локали. Cookies, `Accept-Language`, пользовательские настройки и переключатель языка не выбирают локаль в текущей стратегии Cache Components. Часовой пояс зафиксирован как `UTC` на server и client.

## Разделение UI и REST

Сообщения находятся в парных каталогах `src/messages/*.en.json` и `*.ru.json`. Из них берутся metadata и безопасный текст ошибок API. UI локализует по стабильному Problem Details `code`, не показывает и не разбирает invariant-English `title` или `detail` из API.

Валидация и авторизация ASP.NET Core, `/api/v1/organizations/{organizationId}` и имена generated DTO одинаковы для обеих локалей.

## Маршруты документации

Документы используют варианты `.en.md`/`.ru.md`, а канонические маршруты нейтральны: `/docs/workspace/settings` — один URL на обоих языках. Предпочтительны пары; registry может пометить fallback, если локали не хватает.

## Связанные страницы

- [Локализованный контент документации](/docs/general/authoring/localized-content)
- [Оболочка приложения](/docs/application)
- [Безопасность среды выполнения](/docs/application/runtime-security)
