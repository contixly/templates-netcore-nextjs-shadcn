import type { ComponentType } from "react";
import type { MDXProps } from "mdx/types";
import { useTranslations } from "next-intl";

import { DocumentsPageMeta } from "@/src/features/documents/ui/documents-page-meta";
import { DocumentsPageToc } from "@/src/features/documents/ui/documents-page-toc";
import { createDocumentMdxComponents } from "@/src/features/documents/ui/mdx/documents-mdx-components";
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
    <div className="mx-auto grid w-full max-w-[1400px] grid-cols-1 gap-10 px-4 pt-6 sm:px-6 sm:py-6 lg:px-10 lg:py-10 xl:grid-cols-[minmax(0,1fr)_18rem] xl:gap-12 xl:px-12">
      <article
        aria-labelledby="document-title"
        className="flex w-full min-w-0 flex-col text-sm text-foreground"
      >
        <header className="flex flex-col gap-3">
          <div className="flex flex-col gap-2 sm:pr-24">
            <h1
              className="scroll-m-20 text-2xl leading-tight font-semibold tracking-tight text-balance md:text-3xl"
              id="document-title"
            >
              {document.meta.title}
            </h1>
            <p className="w-full text-sm leading-6 text-pretty text-muted-foreground">
              {document.meta.description}
            </p>
          </div>
          <DocumentsPageMeta document={document} />
        </header>
        <div className="documents-prose mt-8 flex w-full min-w-0 flex-col">
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
