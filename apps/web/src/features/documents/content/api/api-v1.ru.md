---
title: "Справочник API v1"
description: "Стартовые API-маршруты только для чтения, заголовок аутентификации, формат успешного ответа и частые ошибки."
group: "API и интеграции"
groupOrder: 700
parentItem: "Справочник API"
parentItemOrder: 80
order: 10
toc: true
purpose: "Справочник API"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Справочник API v1

Машинная поверхность работает только на чтение. Передавайте ровно один непустой ключ в `x-api-key`; не помещайте его в URL, браузерное хранилище, исходный код, логи или артефакты.

```bash
curl -H "x-api-key: $API_KEY" "$API_ORIGIN/api/v1/me"
```

## Поддерживаемое чтение

| Метод и путь                                                        | Обязательные scopes                                   | Режимы учетных данных |
| ------------------------------------------------------------------- | ----------------------------------------------------- | --------------------- |
| `GET /api/v1/me`                                                    | `basic:read`                                          | Только API-ключ       |
| `GET /api/v1/organizations`                                         | `organization:read`                                   | Cookie или API-ключ   |
| `GET /api/v1/organizations/{organizationId}`                        | `organization:read`                                   | Только API-ключ       |
| `GET /api/v1/organizations/{organizationId}/members`                | `organization:read` + `member:read`                   | Cookie или API-ключ   |
| `GET /api/v1/organizations/{organizationId}/teams`                  | `organization:read` + `team:read`                     | Cookie или API-ключ   |
| `GET /api/v1/organizations/{organizationId}/teams/{teamId}/members` | `organization:read` + `team:read` + `teamMember:read` | Cookie или API-ключ   |

На смешанном маршруте наличие `x-api-key` выбирает только аутентификацию API-ключом; cookie не компенсирует невалидный ключ.

## Envelopes и cursors

Успех использует `{ "data": ... }`. Коллекции используют `{ "data": { "items": [], "nextCursor": null } }`; `limit` по умолчанию `50`, диапазон `1..100`. Передавайте `nextCursor` без изменений как `cursor`. Cursors непрозрачны, имеют версию и зависят от коллекции: не декодируйте, не изменяйте, не создавайте и не переносите их.

Личный ключ действует как пользователь, и текущее участие проверяется в каждом запросе. Ключ организации имеет доступ только к tenant владельца.

## Problem Details

Ошибки используют `application/problem+json`, а не envelope `error`. Обязательны поля RFC Problem Details, стабильный `code` и безопасный `traceId`; validation также добавляет `errors`. Решения принимаются по статусу и `code`.

| Статус | Типичный code                                             | Действие                                |
| ------ | --------------------------------------------------------- | --------------------------------------- |
| `400`  | `invalid_cursor`                                          | Исправьте входные данные.               |
| `401`  | `api_key_missing`, `api_key_invalid`                      | Передайте или замените ключ.            |
| `403`  | `api_key_permission_denied`, `organization_access_denied` | Исправьте scopes или доступ к tenant.   |
| `404`  | Code отсутствующего ресурса                               | Авторизованный ресурс отсутствует.      |
| `429`  | `api_key_rate_limited`                                    | Ждите целое число секунд `Retry-After`. |

## Связанные страницы

- [Управление API-ключами](/docs/api/api-keys)
- [Права и ограничения частоты](/docs/api/permissions-rate-limits)
- [Добавить маршрут API v1](/docs/developers/api-v1-endpoint)
