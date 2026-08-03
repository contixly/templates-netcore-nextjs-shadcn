---
title: "Локализованный контент документации"
description: "Поддержка парных английских и русских исходных файлов по одному каноническому URL и правила публикации и fallback."
group: "Документация"
groupOrder: 400
parentItem: "Подготовка страниц"
parentItemOrder: 100
order: 20
toc: true
purpose: "Справочник для авторов документации"
status: "published"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Локализованный контент документации

Локализация документации кодируется в именах исходных файлов, а не в публичных URL. Локаль
развертывания выбирает подходящий скомпилированный вариант, а ссылки остаются каноническими.

## Пары файлов

Используйте явный поддерживаемый суффикс:

- `page.en.md` и `page.ru.md`;
- `page.en.mdx` и `page.ru.mdx`, когда нужны специальные компоненты;
- `index.en.md` и `index.ru.md` для индекса каталога.

У production-visible канонической страницы должны быть английский и русский варианты. Вариант со
статусом draft или review может временно не иметь пары при локальной подготовке, но так нельзя
опубликовать неполную пару.

## Канонические URL

Compiler удаляет суффикс локали, расширение и конечный сегмент `index`:

| Исходный файл                  | Канонический URL            |
| ------------------------------ | --------------------------- |
| `general/quick-start.en.md`    | `/docs/general/quick-start` |
| `general/quick-start.ru.md`    | `/docs/general/quick-start` |
| `general/glossary/index.en.md` | `/docs/general/glossary`    |
| `general/glossary/index.ru.md` | `/docs/general/glossary`    |

Во внутренних ссылках нет `.en`, `.ru`, `.md` или `.mdx`. Дубли исходных файлов для одного
canonical URL и locale останавливают compilation.

## Семантика пары

Переводите смысл, а не структуру предложения. В паре должны совпадать:

- поддерживаемое поведение, limits, security rules и отложенные возможности;
- иерархия заголовков и порядок шагов;
- методы API, routes, имена полей, codes и команды;
- status, version, `editedAt`, порядок навигации и связанные canonical routes.

Стабильные технические идентификаторы пишите точно как в реализации. Не переводите route,
environment variable, command, code Problem Details, JSON field или имя сгенерированной операции.

## Маркер fallback

Registry хранит доступные локали, а runtime может пометить fallback content, если выбранного
варианта нет. Это помогает при локальной работе над draft/review и безопасно обрабатывает неполный
исходник, но не отменяет правило публикации: у страницы `published` или `archived` должны быть оба
production-visible варианта.

## Проверка пары

Запустите в `apps/web`:

```bash
npm run content:generate
npm run content:check
npm run content:test
```

Затем проверьте diff парных исходников и сгенерированные поисковые записи. Убедитесь, что внутренние
ссылки и fragments заголовков разрешаются в обеих локалях и в канонической ссылке нет суффикса
локали.

## Связанные страницы

- [Как писать документацию](/docs/general/authoring/how-to-write-docs)
- [Компоненты документации](/docs/general/authoring/sample)
- [Локализация](/docs/application/localization)
