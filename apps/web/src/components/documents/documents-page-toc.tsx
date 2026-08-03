"use client";

import { useEffect, useState } from "react";

import {
  DOCUMENTS_SCROLL_CONTAINER_SELECTOR,
  findActiveDocumentHeading,
  readDocumentHash,
} from "@/src/components/documents/documents-scroll-spy";
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

    const update = () => {
      const next = findActiveDocumentHeading(container, headingIds);
      if (next) setActiveId(next);
    };
    update();
    container.addEventListener("scroll", update, { passive: true });
    return () => container.removeEventListener("scroll", update);
  }, [headings]);

  return (
    <nav
      aria-label={label}
      className="sticky top-6 flex flex-col gap-3 text-xs"
    >
      <h2 className="font-semibold">{label}</h2>
      <ul className="flex flex-col gap-2 border-l pl-3">
        {headings.map((heading) => (
          <li key={heading.id}>
            <a
              aria-current={activeId === heading.id ? "location" : undefined}
              className={cn(
                "text-muted-foreground hover:text-foreground",
                activeId === heading.id && "font-medium text-foreground",
              )}
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
