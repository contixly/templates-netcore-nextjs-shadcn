---
title: "Приглашения"
description: "Создание приглашений в рабочее пространство, ссылки, принятие или отклонение приглашений и назначение команды."
group: "Рабочее пространство"
groupOrder: 800
parentItem: "Приглашения"
parentItemOrder: 60
order: 10
toc: true
purpose: "Инструкция по приглашениям в рабочее пространство"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Приглашения

Endpoints ASP.NET Core проверяют подтвержденный email, роли, доменную политику, истечение и нераскрытие.

## Создать приглашение

Owner/admin отправляет `POST /api/v1/organizations/{organizationId}/invitations` с email, доступной ролью и необязательным UUID команды той же организации. Admin приглашает `member`/`admin`, owner также `owner`. API проверяет домен, дубликат, участие, pending-cap, авторизацию, rate limit и CSRF.

`GET /api/v1/organizations/{organizationId}/invitations` принимает `status`, непрозрачный `cursor` и ограниченный `limit` (по умолчанию `50`, `1..100`) только для менеджеров.

## Статусы приглашений

Новое приглашение истекает ровно через 48 часов. Истечение вычисляется при чтении; worker нет. Состояния: `pending`, `accepted`, `rejected`, `canceled` или вычисленное `expired`; cancel/resend не опубликованы.

## Принять или отклонить приглашение

На `/invite/{invitationId}` запрос `GET /api/v1/invitations/{invitationId}` отдает приватные детали только при совпадении текущего основного подтвержденного email. `POST .../accept` или `POST .../reject` требует свежий CSRF. Отсутствующее, чужое или адресованное другому приглашение не раскрывает получателя/организацию.

Accept атомарно создает участие в организации, необязательное участие в команде, accepted-состояние и предпочтение активной организации. Reject меняет только состояние. Истекшее/обработанное нельзя решить повторно.

## Личный список приглашений

`GET /api/v1/account/invitations` дает ограниченный pending-список для `/user/invitations` и `/welcome`. Сейчас доставка - относительный same-origin путь/manual-share fallback. Реальный email, outbox/retry и cancel/resend UI вне scope.

## Связанные страницы

- [Списки разрешенных доменов email](/docs/workspace/email-domains)
- [Команды](/docs/workspace/teams)
- [Пользователь без рабочего пространства](/docs/workspace/no-workspace)
