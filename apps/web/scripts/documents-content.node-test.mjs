import assert from "node:assert/strict";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, resolve } from "node:path";
import { afterEach, test } from "node:test";
import * as contentCompiler from "./documents-content-lib.mjs";
import "./documents-corpus.node-test.mjs";

const {
  checkDocumentsArtifacts,
  compileDocumentsContent,
  writeDocumentsArtifacts,
} = contentCompiler;

const fixtureRoots = [];

afterEach(async () => {
  await Promise.all(
    fixtureRoots
      .splice(0)
      .map((path) => rm(path, { force: true, recursive: true })),
  );
});

const metadata = {
  title: "Document",
  description: "A deterministic documentation fixture.",
  group: "Guides",
  groupOrder: 10,
  parentItem: "Overview",
  parentItemOrder: 10,
  order: 10,
  toc: true,
  purpose: "Test fixture",
  status: "published",
  author: "Test",
  version: "1.0.0",
  editedAt: "2026-08-02",
};

function frontmatter(values, body = "") {
  return `---\n${Object.entries(values)
    .map(([key, value]) => `${key}: ${JSON.stringify(value)}`)
    .join("\n")}\n---\n\n# ${values.title}\n\n${body}`;
}

async function runFixture(name) {
  const root = await mkdtemp(resolve(tmpdir(), `documents-content-${name}-`));
  fixtureRoots.push(root);
  const contentRoot = resolve(root, "content");
  const publicRoot = resolve(root, "public");
  await mkdir(publicRoot, { recursive: true });

  const files = {
    "index.en.mdx": frontmatter({
      ...metadata,
      title: "Home",
      group: "Home",
      groupOrder: 20,
    }),
    "index.ru.mdx": frontmatter({
      ...metadata,
      title: "Главная",
      group: "Главная",
      groupOrder: 20,
    }),
    "guides/start.en.md": frontmatter(
      { ...metadata, title: "Start" },
      "## Details\n",
    ),
    "guides/start.ru.md": frontmatter(
      { ...metadata, title: "Начало" },
      "## Details\n",
    ),
  };

  if (name === "unknown-field") {
    files["guides/start.en.md"] = frontmatter({
      ...metadata,
      unsupported: "no",
    });
  }
  if (name === "reading-minutes") {
    files["guides/start.en.md"] = frontmatter({
      ...metadata,
      readingMinutes: 5,
    });
  }
  if (name === "duplicate-locale") {
    files["guides/start.en.mdx"] = frontmatter({
      ...metadata,
      title: "Duplicate",
    });
  }
  if (name === "bad-date") {
    files["guides/start.en.md"] = frontmatter({
      ...metadata,
      editedAt: "2026-02-30",
    });
  }
  if (name === "missing-locale-suffix") {
    files["guides/implicit.md"] = frontmatter({
      ...metadata,
      title: "Implicit locale",
    });
  }
  if (name === "reserved-og") {
    files["og.en.md"] = frontmatter({ ...metadata, title: "Reserved OG" });
    files["og.ru.md"] = frontmatter({ ...metadata, title: "Reserved OG" });
  }
  if (name === "reserved-og-child") {
    files["og/example.en.md"] = frontmatter({
      ...metadata,
      title: "Reserved OG child",
    });
    files["og/example.ru.md"] = frontmatter({
      ...metadata,
      title: "Reserved OG child",
    });
  }
  if (name === "broken-link") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "[Missing](/docs/missing)\n",
    );
  }
  if (name === "duplicate-link-definition") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      [
        "[Start][destination]",
        "",
        "[destination]: /docs/missing",
        "[destination]: /docs/guides/start",
      ].join("\n"),
    );
  }
  if (name === "broken-fragment") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '<DocumentLinkCard href="/docs/guides/start?from=home#missing" title="Start" />\n',
    );
  }
  if (name === "same-page-fragment-valid") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "## Overview\n\n[Overview](#overview)\n",
    );
  }
  if (name === "same-page-fragment-missing") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "## Overview\n\n[Missing](#missing)\n",
    );
  }
  if (name === "missing-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '![Missing](/img/missing.png "Missing")\n',
    );
  }
  if (name === "unsupported-absolute-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "![Wrong namespace](/images/logo.png)\n",
    );
  }
  if (name === "relative-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "![Relative](../logo.png)\n",
    );
  }
  if (name === "protocol-relative-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "![Protocol relative](//cdn.example.com/logo.png)\n",
    );
  }
  if (name === "data-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "![Embedded](data:image/png;base64,AAAA)\n",
    );
  }
  if (name === "javascript-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "![Executable](javascript:alert(1))\n",
    );
  }
  if (name === "traversal-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "![Outside image namespace](/img/../secret.png)\n",
    );
  }
  if (name === "footnote-invalid-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "A note[^asset].\n\n[^asset]: ![Relative](../logo.png)\n",
    );
  }
  if (name === "referenced-footnote-heading") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      [
        "Use it[^note].",
        "",
        "[^note]:",
        "    ## Duplicate",
        "",
        "## Duplicate",
      ].join("\n"),
    );
  }
  if (name === "unreferenced-footnote-heading") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "[^unused]:\n    ### Hidden\n",
    );
  }
  if (name === "ordinary-footnote") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "Use it[^note].\n\n[^note]: Ordinary text.\n",
    );
  }
  if (name === "duplicate-image-definition-in-footnote") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      [
        "A note[^asset].",
        "",
        "[^asset]: ![Logo][asset]",
        "",
        "[asset]: ../missing.png",
        "[asset]: https://cdn.example.com/logo.png",
      ].join("\n"),
    );
  }
  if (name === "duplicate-empty-image-definition-in-footnote") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      [
        "A note[^asset].",
        "",
        "[^asset]: ![Logo][asset]",
        "",
        "[asset]: <>",
        "[asset]: https://cdn.example.com/logo.png",
      ].join("\n"),
    );
  }
  if (name === "srcset-image") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '<img alt="Responsive" src="https://cdn.example.com/base.png" srcSet="javascript:alert(1) 2x" />\n',
    );
  }
  if (name === "external-http-images") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      [
        "![HTTPS](HTTPS://cdn.example.com/logo.png)",
        '<img alt="HTTP" src="HTTP://cdn.example.com/logo.png" />',
      ].join("\n"),
    );
  }
  if (name === "published-missing-ru") {
    delete files["guides/start.ru.md"];
  }
  if (
    name === "published-link-to-draft" ||
    name === "published-link-to-review" ||
    name === "published-link-to-hidden"
  ) {
    const status = name.endsWith("draft")
      ? "draft"
      : name.endsWith("review")
        ? "review"
        : "published";
    const hide = name.endsWith("hidden") ? true : undefined;
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "[Start](/docs/guides/start)\n",
    );
    files["guides/start.en.md"] = frontmatter(
      {
        ...metadata,
        title: "Start",
        status,
        ...(hide === undefined ? {} : { hide }),
      },
      "## Details\n",
    );
    files["guides/start.ru.md"] = frontmatter(
      {
        ...metadata,
        title: "Начало",
        status: "published",
      },
      "## Details\n",
    );
  }
  if (name === "unknown-component") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "<UnknownComponent />\n",
    );
  }
  if (name === "member-component") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "<Steps.Unknown />\n",
    );
  }
  if (name === "namespaced-component") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "<Steps:Unknown />\n",
    );
  }
  if (name === "lowercase-member-component") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "<foo.bar />\n",
    );
  }
  if (name === "lowercase-namespaced-component") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "<svg:path />\n",
    );
  }
  if (name === "unicode-uppercase-component") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "<Компонент />\n",
    );
  }
  if (name === "unsafe-script-element") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '<script src="https://evil.example/script.js" />\n',
    );
  }
  if (name === "unsafe-iframe-element") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '<iframe src="https://evil.example/embed" />\n',
    );
  }
  if (name === "content-export") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "export const unsafe = true\n",
    );
  }
  if (name === "mdx-flow-expression") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "{process.env.SECRET}\n",
    );
  }
  if (name === "mdx-text-expression") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "Secret: {process.env.SECRET}\n",
    );
  }
  if (name === "mdx-expression-attribute") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '<Callout title={process.env.SECRET} variant="info">Unsafe</Callout>\n',
    );
  }
  if (name === "mdx-spread-attribute") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "<Callout {...process.env}>Unsafe</Callout>\n",
    );
  }
  if (name === "mdx-literal-attributes") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '<Callout title="Safe" variant="info">Literal content</Callout>\n',
    );
  }
  if (name === "reserved-footnote-attribute") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      '## <span data-footnote-ref="">Heading</span>\n',
    );
  }
  if (name === "multiline-export-prose") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "export\nconst unsafe = true\n",
    );
  }
  if (name === "ordinary-import-prose") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "import\nA plain-language continuation for documentation authors.\n",
    );
  }
  if (name === "commented-content-export") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "export /* compiler boundary */ const unsafe = true\n",
    );
  }
  if (name === "commented-content-import") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      'import /* compiler boundary */ { readFile } from "node:fs/promises"\n',
    );
  }
  if (name === "inline-module-code") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      "Use `export const example = true` as an explanatory inline sample.\n",
    );
  }
  if (name === "fence-separated-module-tokens") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      [
        "export",
        "```text",
        "This fence separates the surrounding prose.",
        "```",
        "const remains explanatory prose.",
      ].join("\n"),
    );
  }
  if (name === "fenced-content") {
    files["index.en.mdx"] = frontmatter(
      { ...metadata, title: "Home", group: "Home", groupOrder: 20 },
      [
        "~~~mdx",
        "## Not a heading",
        "[Missing](/docs/missing#missing)",
        "![Missing](/img/missing.png)",
        "<UnknownComponent />",
        "export const example = true",
        "~~~~",
        "",
        "[Start][start]",
        "[start]: /docs/guides/start/index/?from=fixture#details",
      ].join("\n"),
    );
  }

  await Promise.all(
    Object.entries(files).map(async ([relativePath, source]) => {
      const path = resolve(contentRoot, relativePath);
      await mkdir(dirname(path), { recursive: true });
      await writeFile(path, source, "utf8");
    }),
  );
  if (name === "traversal-image") {
    await writeFile(resolve(publicRoot, "secret.png"), "not an image", "utf8");
  }

  return {
    result: await compileDocumentsContent({ contentRoot, publicRoot }),
    contentRoot,
    publicRoot,
    root,
  };
}

test("discovers localized variants in deterministic navigation order", async () => {
  const { result } = await runFixture("valid");

  assert.equal(result.documents.length, 4);
  assert.equal(result.documents[0].canonicalUrl, "index");
  assert.deepEqual(result.documents[0].availableLocales, ["en", "ru"]);
  assert.deepEqual(result.diagnostics, []);
  assert.equal(result.searchIndexJson.includes("\r"), false);
  assert.match(result.registrySource, /export const documents/);
});

test("rejects unknown frontmatter fields", async () => {
  await assert.rejects(
    async () => (await runFixture("unknown-field")).result,
    /documents_metadata_unknown_field/,
  );
});

test("rejects readingMinutes outside the frontmatter allow-list", async () => {
  await assert.rejects(
    async () => (await runFixture("reading-minutes")).result,
    /documents_metadata_unknown_field/,
  );
});

test("rejects duplicate localized variants", async () => {
  await assert.rejects(
    async () => (await runFixture("duplicate-locale")).result,
    /documents_duplicate_locale/,
  );
});

test("rejects invalid editedAt dates", async () => {
  await assert.rejects(
    async () => (await runFixture("bad-date")).result,
    /documents_metadata_invalid_edited_at/,
  );
});

test("rejects content sources without an explicit supported locale suffix", async () => {
  await assert.rejects(
    async () => (await runFixture("missing-locale-suffix")).result,
    /documents_missing_locale_suffix: guides\/implicit\.md must include an explicit \.en or \.ru suffix/,
  );
});

for (const [name, path, slug] of [
  ["reserved-og", "og.en.md", "og"],
  ["reserved-og-child", "og/example.en.md", "og/example"],
]) {
  test(`rejects the exact OG route's reserved slug ${slug}`, async () => {
    await assert.rejects(
      async () => (await runFixture(name)).result,
      (error) => {
        assert.equal(
          error.message,
          `documents_reserved_slug: ${path} maps to reserved canonical URL ${slug}`,
        );
        return true;
      },
    );
  });
}

test("extracts stable duplicate heading identifiers outside code fences", () => {
  assert.deepEqual(
    contentCompiler.extractHeadings?.(
      "## Same\n```md\n## Hidden\n```\n## Same",
    ),
    [
      { level: 2, title: "Same", id: "same" },
      { level: 2, title: "Same", id: "same-2" },
    ],
  );
});

test("decodes Markdown character references before generating heading IDs", () => {
  assert.deepEqual(contentCompiler.extractHeadings?.("## API &amp; UI"), [
    { level: 2, title: "API & UI", id: "api-ui" },
  ]);
});

test("extracts nested MDX headings recursively in source order", () => {
  assert.deepEqual(
    contentCompiler.extractHeadings?.(
      "<Callout>\n\n## API &amp; UI\n\n</Callout>\n\n### Tail",
    ),
    [
      { level: 2, title: "API & UI", id: "api-ui" },
      { level: 3, title: "Tail", id: "tail" },
    ],
  );
});

test("keeps compiler heading IDs aligned for footnote references and images", () => {
  assert.deepEqual(
    contentCompiler.extractHeadings?.(
      "## Heading[^note]\n\n[^note]: Note\n\n## ![Logo](/img/logo.png)",
    ),
    [
      { level: 2, title: "Heading", id: "heading" },
      { level: 2, title: "", id: "section" },
    ],
  );
});

test("rejects broken internal document links", async () => {
  await assert.rejects(
    async () => (await runFixture("broken-link")).result,
    /documents_broken_link/,
  );
});

test("uses the first duplicate Markdown link definition like the renderer", async () => {
  await assert.rejects(
    async () => (await runFixture("duplicate-link-definition")).result,
    /documents_broken_link: index\.en\.mdx:\d+ -> \/docs\/missing/,
  );
});

test("rejects links to matching-locale draft targets with a published sibling", async () => {
  await assert.rejects(
    async () => (await runFixture("published-link-to-draft")).result,
    /documents_unpublished_link: index\.en\.mdx:\d+ -> \/docs\/guides\/start/,
  );
});

test("rejects links to matching-locale review targets with a published sibling", async () => {
  await assert.rejects(
    async () => (await runFixture("published-link-to-review")).result,
    /documents_unpublished_link: index\.en\.mdx:\d+ -> \/docs\/guides\/start/,
  );
});

test("rejects links to matching-locale hidden targets with a published sibling", async () => {
  await assert.rejects(
    async () => (await runFixture("published-link-to-hidden")).result,
    /documents_unpublished_link: index\.en\.mdx:\d+ -> \/docs\/guides\/start/,
  );
});

test("rejects links to missing generated heading fragments", async () => {
  await assert.rejects(
    async () => (await runFixture("broken-fragment")).result,
    /documents_broken_fragment/,
  );
});

test("accepts same-document links to generated heading fragments", async () => {
  const { result } = await runFixture("same-page-fragment-valid");

  assert.deepEqual(result.diagnostics, []);
});

test("rejects same-document links to missing heading fragments", async () => {
  await assert.rejects(
    async () => (await runFixture("same-page-fragment-missing")).result,
    /documents_broken_fragment/,
  );
});

test("rejects missing repository-local images", async () => {
  await assert.rejects(
    async () => (await runFixture("missing-image")).result,
    /documents_missing_image/,
  );
});

for (const name of [
  "unsupported-absolute-image",
  "relative-image",
  "protocol-relative-image",
  "data-image",
  "javascript-image",
  "traversal-image",
]) {
  test(`rejects unsupported image source ${name}`, async () => {
    await assert.rejects(
      async () => (await runFixture(name)).result,
      /documents_invalid_image_source/,
    );
  });
}

for (const name of ["unsafe-script-element", "unsafe-iframe-element"]) {
  test(`rejects executable intrinsic MDX element ${name}`, async () => {
    await assert.rejects(
      async () => (await runFixture(name)).result,
      /documents_unknown_mdx_component/,
    );
  });
}

test("allows case-insensitive external HTTP and HTTPS image schemes", async () => {
  const { result } = await runFixture("external-http-images");

  assert.deepEqual(result.diagnostics, []);
});

test("validates images nested in GFM footnote definitions", async () => {
  await assert.rejects(
    async () => (await runFixture("footnote-invalid-image")).result,
    /documents_invalid_image_source/,
  );
});

for (const name of [
  "referenced-footnote-heading",
  "unreferenced-footnote-heading",
]) {
  test(`rejects h2/h3 inside a GFM footnote definition: ${name}`, async () => {
    await assert.rejects(
      async () => (await runFixture(name)).result,
      /documents_footnote_heading_unsupported: index\.en\.mdx:\d+ contains h2\/h3 inside a GFM footnote definition/,
    );
  });
}

test("continues to support ordinary GFM footnote content", async () => {
  const { result } = await runFixture("ordinary-footnote");

  assert.deepEqual(result.diagnostics, []);
});

test("uses the first duplicate image definition inside a GFM footnote", async () => {
  await assert.rejects(
    async () =>
      (await runFixture("duplicate-image-definition-in-footnote")).result,
    /documents_invalid_image_source: index\.en\.mdx:\d+ -> \.\.\/missing\.png/,
  );
});

test("does not discard a first empty image definition in a GFM footnote", async () => {
  await assert.rejects(
    async () =>
      (await runFixture("duplicate-empty-image-definition-in-footnote")).result,
    /documents_invalid_image_source/,
  );
});

test("rejects literal srcSet instead of bypassing image-source validation", async () => {
  await assert.rejects(
    async () => (await runFixture("srcset-image")).result,
    /documents_mdx_srcset_unsupported/,
  );
});

test("requires both locales for production-visible documents", async () => {
  await assert.rejects(
    async () => (await runFixture("published-missing-ru")).result,
    /documents_missing_published_locale/,
  );
});

test("rejects MDX components outside the closed component set", async () => {
  await assert.rejects(
    async () => (await runFixture("unknown-component")).result,
    /documents_unknown_mdx_component/,
  );
});

test("rejects JSX member expressions that start with an allowed MDX component", async () => {
  await assert.rejects(
    async () => (await runFixture("member-component")).result,
    /documents_unknown_mdx_component/,
  );
});

test("rejects JSX namespaced expressions that start with an allowed MDX component", async () => {
  await assert.rejects(
    async () => (await runFixture("namespaced-component")).result,
    /documents_unknown_mdx_component/,
  );
});

for (const name of [
  "lowercase-member-component",
  "lowercase-namespaced-component",
  "unicode-uppercase-component",
]) {
  test(`rejects closed-set bypass ${name}`, async () => {
    await assert.rejects(
      async () => (await runFixture(name)).result,
      /documents_unknown_mdx_component/,
    );
  });
}

test("rejects executable MDX imports and exports", async () => {
  await assert.rejects(
    async () => (await runFixture("content-export")).result,
    /documents_mdx_module_syntax/,
  );
});

for (const name of ["mdx-flow-expression", "mdx-text-expression"]) {
  test(`rejects executable ${name}`, async () => {
    await assert.rejects(
      async () => (await runFixture(name)).result,
      /documents_mdx_expression_syntax: index\.en\.mdx:\d+ contains forbidden executable expression syntax/,
    );
  });
}

for (const name of ["mdx-expression-attribute", "mdx-spread-attribute"]) {
  test(`rejects executable ${name}`, async () => {
    await assert.rejects(
      async () => (await runFixture(name)).result,
      /documents_mdx_expression_attribute: index\.en\.mdx:\d+ contains forbidden expression-valued JSX attribute syntax/,
    );
  });
}

test("accepts string-valued attributes on allowed MDX components", async () => {
  const { result } = await runFixture("mdx-literal-attributes");

  assert.deepEqual(result.diagnostics, []);
});

test("rejects author-supplied data-footnote-ref attributes", async () => {
  await assert.rejects(
    async () => (await runFixture("reserved-footnote-attribute")).result,
    /documents_reserved_footnote_attribute: index\.en\.mdx:\d+ contains reserved data-footnote-ref syntax/,
  );
});

test("accepts multiline export words that MDX parses as prose", async () => {
  const { result } = await runFixture("multiline-export-prose");

  assert.deepEqual(result.diagnostics, []);
});

test("accepts an ordinary import word followed by prose", async () => {
  const { result } = await runFixture("ordinary-import-prose");

  assert.deepEqual(result.diagnostics, []);
});

test("rejects executable MDX exports separated from syntax by comments", async () => {
  await assert.rejects(
    async () => (await runFixture("commented-content-export")).result,
    /documents_mdx_module_syntax: index\.en\.mdx:\d+ contains forbidden import\/export syntax/,
  );
});

test("rejects executable MDX imports separated from syntax by comments", async () => {
  await assert.rejects(
    async () => (await runFixture("commented-content-import")).result,
    /documents_mdx_module_syntax: index\.en\.mdx:\d+ contains forbidden import\/export syntax/,
  );
});

test("accepts import and export syntax inside inline code", async () => {
  const { result } = await runFixture("inline-module-code");

  assert.deepEqual(result.diagnostics, []);
});

test("does not combine module-like tokens across a code fence", async () => {
  const { result } = await runFixture("fence-separated-module-tokens");

  assert.deepEqual(result.diagnostics, []);
});

test("ignores headings, links, images, and MDX syntax in either fence style", async () => {
  const { result } = await runFixture("fenced-content");

  assert.deepEqual(result.diagnostics, []);
  assert.equal(
    result.documents.find((document) => document.sourcePath === "index.en.mdx")
      .headings.length,
    0,
  );
});

test("writes deterministic artifacts and detects missing or stale output", async () => {
  const fixture = await runFixture("artifacts");
  const registryPath = resolve(
    fixture.root,
    "generated",
    "documents-registry.gen.ts",
  );
  const searchIndexPath = resolve(
    fixture.root,
    "contracts",
    "search-index.json",
  );
  const options = { ...fixture, registryPath, searchIndexPath };

  await assert.rejects(
    checkDocumentsArtifacts(options),
    /documents_artifact_missing/,
  );
  await writeDocumentsArtifacts(options);
  await checkDocumentsArtifacts(options);
  await writeFile(searchIndexPath, "stale\n", "utf8");
  await assert.rejects(
    checkDocumentsArtifacts(options),
    /documents_artifact_stale/,
  );
});
