---
title: "Как писать документацию"
description: "Подготовка парных публичных страниц со строгим frontmatter, закрытым набором MDX-компонентов, проверяемыми ссылками и изображениями и детерминированными артефактами."
group: "Документация"
groupOrder: 400
parentItem: "Подготовка страниц"
parentItemOrder: 100
order: 10
status: "published"
toc: true
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Как писать документацию

Исходные страницы находятся в `apps/web/src/features/documents/content`. Посвящайте каждую
каноническую страницу одной понятной цели пользователя, возможности, теме объяснения или справки и
создавайте английский и русский варианты вместе до публикации.

## Используйте строгий frontmatter

Каждой перенесенной production-странице нужна такая форма:

```md
---
title: "Название страницы"
description: "Короткое описание для заголовка и поиска"
group: "Общее"
groupOrder: 400
parentItem: "Подготовка страниц"
parentItemOrder: 100
order: 10
status: "published"
toc: true
purpose: "Авторы контента"
author: "Команда шаблона"
version: "1.2.0"
editedAt: "2026-08-03"
---
```

`title`, `description`, `group` и `parentItem` должны быть непустыми строками; `order` — конечным
числом; `toc` — boolean; `status` — одним из `draft`, `review`, `published` или `archived`.
Production corpus также содержит непустые `author` и `version` и реальную ISO-дату `editedAt` без
времени.

Список необязательных разрешенных полей: `groupOrder`, `parentItemOrder`, `purpose`, `hide`,
`reading` и `source`. Неизвестные поля, невалидные типы или даты, дубли одного canonical URL/locale
и production-visible страница без пары останавливают compilation. Статусы `published` и `archived`
видны в production, если не задано `hide: true`; `draft` и `review` не видны.

## Именуйте и структурируйте пару

Используйте `page.en.md` и `page.ru.md` или `.mdx`, если странице нужен специальный компонент.
Варианты индекса называются `index.en.md` и `index.ru.md`. В обеих локалях должны совпадать факты,
иерархия заголовков, статус, положение в навигации и связанные маршруты.

Используйте стабильную структуру страницы: назначение, понятия или prerequisites, шаги задачи или
справочные факты, ограничения и ошибки, проверка и связанные страницы. Правое оглавление строится
по заголовкам `##` и `###`.

## Используйте закрытый набор MDX

Предпочитайте обычный Markdown. В MDX разрешены только такие специальные компоненты:

- `Callout`;
- `Steps` и `Step`;
- `Files`, `Folder` и `File`;
- `Tabs` и `Tab`;
- `DocumentLinkGrid`, `DocumentLinkGroup` и `DocumentLinkCard`.

Исполняемые конструкции MDX `import` и `export` и неизвестные компоненты останавливают compilation.
Используйте [Компоненты документации](/docs/general/authoring/sample) как живой fixture
поддерживаемого рендеринга.

## Добавляйте ссылки и изображения

Внутренние ссылки используют канонические URL `/docs/...` без расширения файла и суффикса локали.
Compiler проверяет существование целевой страницы и соответствие fragment идентификатору
сгенерированного заголовка. В связанных разделах английского и русского вариантов сохраняйте
одинаковые маршруты.

Для внешнего материала используйте обычные ссылки `https://`. Локальные изображения документации
задаются абсолютным путем `/img/...` к реальному файлу в `apps/web/public/img`; отсутствующее
изображение останавливает compilation. Пишите содержательный alt text и не помещайте secrets,
credentials или пользовательские данные в примеры и image artifacts.

## Генерируйте и проверяйте артефакты

В `apps/web` выполните:

```bash
npm run content:generate
npm run content:check
npm run content:test
```

`npm run content:generate` перезаписывает зафиксированный TypeScript registry и JSON search index.
`npm run content:check` повторяет compilation и побайтно сравнивает оба результата. Никогда не
редактируйте вручную `src/features/documents/generated/documents-registry.gen.ts` или
`contracts/documents/search-index.json`.

Перед завершением также запустите требуемые точечные тесты MDX/rendering, typecheck, полный Jest
suite и production build.

## Связанные страницы

- [Локализованный контент документации](/docs/general/authoring/localized-content)
- [Компоненты документации](/docs/general/authoring/sample)
- [Требования, E2E и документация](/docs/developers/openspec-e2e-docs)
- [Релизы и журнал изменений](/docs/developers/releases-changelog)
