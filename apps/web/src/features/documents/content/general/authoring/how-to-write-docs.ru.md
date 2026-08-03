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
version: "1.2.1"
editedAt: "2026-08-03"
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

Каждый segment каталога и имени файла для маршрута состоит из строчных ASCII-букв и цифр с
одиночными разделителями `-` или `.`; точки сохраняют version routes вроде `0.0.11`. Uppercase, `_`,
Unicode, пробелы, percent escapes, URL delimiters и backslashes останавливают compilation до
генерации маршрута.
Compiler удаляет один conventional terminal `/index`; повторные terminal aliases вроде
`index/index` или `guide/index/index` неоднозначны и останавливают compilation.

Используйте стабильную структуру страницы: назначение, понятия или prerequisites, шаги задачи или
справочные факты, ограничения и ошибки, проверка и связанные страницы. Правое оглавление строится
по заголовкам `##` и `###`, в том числе внутри поддерживаемых MDX-контейнеров. Номера footnote
references и изображения не входят в heading ID, поэтому каждый заголовок должен содержать текст.
Не помещайте `##` или `###` внутрь footnote definition: GFM переносит используемые footnotes и
удаляет неиспользуемые. Обычные paragraphs, lists, links, code и images в footnote поддерживаются.
Не помещайте `##` или `###` на любой глубине внутри `Tabs` или `Tab`: содержимое неактивного tab не
смонтировано и не может владеть опубликованным fragment. `Tabs` содержит только непосредственные
элементы `Tab`, а каждый `Tab` должен непосредственно принадлежать `Tabs`; прямой prose или wrapper
component останавливает compilation. Каждый `Tabs` содержит хотя бы один tab, непосредственные
`Tab.value` уникальны, а заданный непустой `Tabs.defaultValue` в точности совпадает с одним из них.
Page/runtime markup резервирует
`document-title`, `main-content` и `footnote-label`, поэтому заголовок с таким normalized ID
начинается с `-2`, а generated suffix никогда не повторяет ID другого заголовка. Heading base из
динамических GFM namespaces `user-content-fn-`/`user-content-fnref-` получает prefix
`document-heading-`.

## Используйте закрытый набор MDX

Предпочитайте обычный Markdown. В MDX разрешены только такие специальные компоненты:

- `Callout`;
- `Steps` и `Step`;
- `Files`, `Folder` и `File`;
- `Tabs` и `Tab`;
- `DocumentLinkGrid`, `DocumentLinkGroup` и `DocumentLinkCard`.

Исполняемые конструкции MDX `import` и `export`, flow/text expressions, JSX spreads,
expression-valued attributes, executable elements вроде `script`/`iframe` и неизвестные компоненты
останавливают compilation. JSX attributes используют закрытый per-element contract и должны быть
строками в кавычках: boolean shorthand, неизвестные attributes, отсутствующие или пустые
обязательные attributes компонентов, повторные имена attributes и невалидные variants останавливают
compilation до rendering. Используйте
[Компоненты документации](/docs/general/authoring/sample) как живой fixture поддерживаемого
рендеринга.
JSX-атрибут `data-footnote-ref` зарезервирован для anchors, создаваемых GFM.

## Добавляйте ссылки и изображения

Внутренние ссылки используют канонические URL `/docs/...` без расширения файла и суффикса локали.
Compiler проверяет существование целевой страницы и соответствие fragment идентификатору
сгенерированного заголовка. Query string не участвует в target lookup, а любое число trailing
slashes одинаково нормализуется при compilation и rendering. В связанных разделах английского и русского вариантов сохраняйте
одинаковые маршруты.
`/docs/index` — единственный root alias; nested `/docs/.../index` и повторный `/docs/index/index`
отклоняются, потому что соответствующего публичного route нет. Percent-encoded segments пути
документа, включая encoded-написание канонических символов, неканоничны и отклоняются.

Не дублируйте Markdown reference labels. Если label определён несколько раз, первая definition
имеет приоритет, как и при MDX rendering.

Для внешнего материала используйте обычные ссылки `https://`. Изображения используют явный
`http://` или `https://` source либо абсолютный путь `/img/...` к реальному файлу в
`apps/web/public/img`. Относительные пути, protocol-relative sources, другие локальные namespaces и
отсутствующие изображения останавливают compilation. Пишите содержательный alt text и не помещайте
secrets, credentials или пользовательские данные в примеры и image artifacts. Используйте один
`src`: `srcSet` не поддерживается.

Link target не должен содержать внешние пробелы и обязан разрешаться в HTTP(S) или `mailto:`;
исполняемые и malformed protocols останавливают compilation и повторно блокируются при rendering.

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
