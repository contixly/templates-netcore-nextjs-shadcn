"use client";

import { IconMenu3 } from "@tabler/icons-react";
import { useEffect, useState } from "react";

import {
  DOCUMENTS_SCROLL_CONTAINER_SELECTOR,
  findActiveDocumentHeading,
  readDocumentHash,
} from "@/src/features/documents/ui/documents-scroll-spy";
import type { DocumentHeading } from "@/src/features/documents/documents-types";
import { cn } from "@/src/lib/utils";

export function DocumentsPageToc({
  headings,
  label,
}: Readonly<{ headings: DocumentHeading[]; label: string }>) {
  const [activeId, setActiveId] = useState(headings[0]?.id);

  useEffect(() => {
    const headingIds = headings.map(({ id }) => id);
    const container = document.querySelector<HTMLElement>(
      DOCUMENTS_SCROLL_CONTAINER_SELECTOR,
    );
    if (!container) return;

    const fragment = readDocumentHash(window.location.hash);
    if (fragment && headingIds.includes(fragment)) {
      window.requestAnimationFrame(() => setActiveId(fragment));
    }

    const updateFromHash = () => {
      const nextFragment = readDocumentHash(window.location.hash);
      if (nextFragment && headingIds.includes(nextFragment)) {
        setActiveId(nextFragment);
      }
    };

    const update = () => {
      const next = findActiveDocumentHeading(container, headingIds);
      if (next) setActiveId(next);
    };
    update();
    container.addEventListener("scroll", update, { passive: true });
    window.addEventListener("hashchange", updateFromHash);
    return () => {
      container.removeEventListener("scroll", update);
      window.removeEventListener("hashchange", updateFromHash);
    };
  }, [headings]);

  return (
    <nav
      aria-label={label}
      className="sticky top-9 flex max-h-[calc(100vh-4rem)] w-full flex-col self-start overflow-y-auto text-sm text-muted-foreground"
    >
      <h2 className="mb-3 flex h-6 items-center gap-1 text-xs font-normal">
        <IconMenu3 aria-hidden="true" size={16} />
        {label}
      </h2>
      <ul className="flex flex-col gap-1 border-l border-dashed border-muted-foreground/60">
        {headings.map((heading) => (
          <li key={heading.id}>
            <a
              aria-current={activeId === heading.id ? "location" : undefined}
              className={cn(
                "block py-2 pl-3 text-sm text-muted-foreground no-underline transition-colors hover:text-foreground",
                activeId === heading.id &&
                  "-ml-0.5 border-l-2 border-foreground font-bold text-foreground",
              )}
              data-active={activeId === heading.id}
              data-depth="2"
              href={`#${heading.id}`}
            >
              {heading.title}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
}
