---
title: "OAuth-провайдеры"
description: "Настройка учетных данных провайдеров и поведение UI входа и подключений только для настроенных провайдеров."
group: "Приложение"
groupOrder: 500
parentItem: "Авторизация"
parentItemOrder: 80
order: 10
toc: true
purpose: "Инструкция по авторизации"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# OAuth-провайдеры

Внешним входом владеет ASP.NET Core через OpenIddict Client. Закрытый набор: Google, GitHub, GitLab, VK и Yandex. OpenIddict — client и граница state/replay, а не authorization server или token vault.

## Настройка провайдеров

Задайте HTTPS `ExternalAuthentication__PublicOrigin`; HTTP разрешен только для loopback разработки. Настройте одновременно `ClientId` и `ClientSecret` в `ExternalAuthentication__Providers__Google`, `GitHub`, `GitLab`, `Vk` или `Yandex`.

Провайдер объявляется только с полной канонической парой. Ноль провайдеров допустим; частичная или неизвестная конфигурация не проходит validation без записи секретов в лог.

## Callback paths

| Провайдер | Callback                           |
| --------- | ---------------------------------- |
| Google    | `/api/auth/callback/google`        |
| GitHub    | `/api/auth/callback/github`        |
| GitLab    | `/api/auth/callback/gitlab`        |
| VK        | `/api/auth/callback/vk`            |
| Yandex    | `/api/auth/oauth2/callback/yandex` |

Эти неверсионированные protocol callbacks исключены из OpenAPI и generated REST SDK. Next.js начинает sign-in/connect только через `POST /api/v1/auth/external/{provider}/challenge` со свежим CSRF и безопасным same-origin return path, затем выполняет top-level переход на выданный сервером HTTPS URL.

## Граница токенов

Успех создает защищенную сессию с `HttpOnly`; JavaScript не хранит bearer token. Provider access/refresh tokens существуют только при callback normalization и не сохраняются в Identity, строках OpenIddict, базе, логах, ответах или браузерном storage. Поэтому локальное отключение не отзывает remote consent, а refresh provider token не поддерживается.

## Связанные страницы

- [Профиль и подключения](/docs/account/profile-connections)
- [Оболочка приложения](/docs/application)
- [Безопасность среды выполнения](/docs/application/runtime-security)
