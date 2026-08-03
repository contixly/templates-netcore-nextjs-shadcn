---
title: "Права и ограничения частоты"
description: "Как права API-ключей, наборы прав, срок действия и ограничения частоты управляют доступом для интеграций."
group: "API и интеграции"
groupOrder: 700
parentItem: "Права"
parentItemOrder: 70
order: 10
toc: true
purpose: "Справочник API"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Права и ограничения частоты

API-ключи получают только закрытые read-разрешения итерации 7. Клиенты выбирают ID presets и не отправляют произвольные scopes.

## Закрытые presets

| Preset                           | Раскрываемые scopes                                                 |
| -------------------------------- | ------------------------------------------------------------------- |
| `basic-read`                     | `basic:read`                                                        |
| `organization-read`              | `organization:read`                                                 |
| `organization-members-read`      | `organization:read`, `member:read`                                  |
| `organization-teams-read`        | `organization:read`, `team:read`                                    |
| `organization-team-members-read` | `organization:read`, `team:read`, `teamMember:read`                 |
| `organization-read-all`          | все read scopes организации, участников, команд и участников команд |

Нужен хотя бы один preset. ID и scopes — закрытые наборы с учетом регистра. Неизвестные значения и raw scopes отклоняются. Сейчас нет машинных write scopes или мутаций.

## Изоляция tenant

Личный ключ проверяется по текущему участию владельца в каждом запросе к организации. Ключ организации привязан ровно к одной организации. Scopes не обходят эту границу. Чтение команды без `teamMember:read` возвращает безопасные счетчики без встроенных данных участников.

## Срок и фиксированные окна

Срок: `never`, `7d`, `30d`, `90d` или `365d`. Rate limiting опционален для каждого ключа: окно `1m`, `1h` или `1d`; максимум — целое `1..1000000`. Каждое предъявление валидного ключа расходует единицу, даже если позже не пройдет авторизация.

Ограниченный запрос возвращает `429 api_key_rate_limited`, `Cache-Control: no-store` и целое число секунд `Retry-After` от `1` до `86400`. Не повторяйте раньше. Невалидные credentials не раскрывают существование ключа.

## Связанные страницы

- [API-доступ](/docs/api)
- [Справочник API v1](/docs/api/api-v1)
- [Управление API-ключами](/docs/api/api-keys)
