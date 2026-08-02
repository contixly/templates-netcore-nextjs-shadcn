import type { ComponentType } from "react";
import type { MDXProps } from "mdx/types";
import { useTranslations } from "next-intl";

import { DocumentsPageMeta } from "@/src/components/documents/documents-page-meta";
import { DocumentsPageToc } from "@/src/components/documents/documents-page-toc";
import { createDocumentMdxComponents } from "@/src/components/documents/mdx/documents-mdx-components";
import type { DocumentInfo } from "@/src/features/documents/documents-types";

export function DocumentsPage({
  Content,
  document,
}: Readonly<{ Content: ComponentType<MDXProps>; document: DocumentInfo }>) {
  const t = useTranslations("documents.page");
  const headings = document.meta.toc
    ? document.headings.filter(({ level }) => level === 2)
    : [];

  return (
    <div className="grid min-w-0 gap-12 xl:grid-cols-[minmax(0,1fr)_12rem]">
      <article aria-labelledby="document-title" className="min-w-0">
        <header className="flex flex-col gap-5">
          <div className="flex flex-col gap-2">
            <h1
              className="text-3xl font-semibold tracking-tight"
              id="document-title"
            >
              {document.meta.title}
            </h1>
            <p className="text-sm/relaxed text-muted-foreground">
              {document.meta.description}
            </p>
          </div>
          <DocumentsPageMeta document={document} />
        </header>
        <div className="documents-prose mt-10">
          <Content components={createDocumentMdxComponents(document)} />
        </div>
      </article>
      {headings.length > 0 ? (
        <aside className="hidden xl:block">
          <DocumentsPageToc headings={headings} label={t("onThisPage")} />
        </aside>
      ) : null}
    </div>
  );
}
