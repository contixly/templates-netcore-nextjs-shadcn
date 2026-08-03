"use client";

import { IconChevronRight } from "@tabler/icons-react";
import type { Route } from "next";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useState } from "react";

import type { DocumentsSidebarGroup } from "@/src/features/documents/documents-types";
import { cn } from "@/src/lib/utils";

export function DocumentsSidebar({
  currentHref,
  navigation,
  onNavigate,
}: Readonly<{
  currentHref: string;
  navigation: DocumentsSidebarGroup[];
  onNavigate?: () => void;
}>) {
  const t = useTranslations("documents");

  return (
    <nav aria-label={t("navigation.label")} className="flex flex-col gap-6">
      <h2 className="font-semibold">{t("sidebar.title")}</h2>
      {navigation.map((group) => (
        <section className="flex flex-col gap-2" key={group.label}>
          <h3 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
            {group.label}
          </h3>
          <div className="flex flex-col gap-1">
            {group.parents.map((parent) => (
              <DocumentsSidebarParent
                currentHref={currentHref}
                key={parent.label}
                label={parent.label}
                items={parent.items}
                onNavigate={onNavigate}
              />
            ))}
          </div>
        </section>
      ))}
    </nav>
  );
}

function DocumentsSidebarParent({
  currentHref,
  items,
  label,
  onNavigate,
}: Readonly<{
  currentHref: string;
  items: DocumentsSidebarGroup["parents"][number]["items"];
  label: string;
  onNavigate?: () => void;
}>) {
  const active = items.some((item) => item.href === currentHref);
  const [expanded, setExpanded] = useState(false);

  return (
    <details
      className="group"
      onToggle={(event) => setExpanded(event.currentTarget.open)}
      open={active || expanded}
    >
      <summary className="flex cursor-pointer list-none items-center gap-1 py-1 text-sm font-medium outline-none hover:text-foreground focus-visible:ring-1 focus-visible:ring-ring [&::-webkit-details-marker]:hidden">
        <IconChevronRight
          aria-hidden="true"
          className="transition-transform group-open:rotate-90"
        />
        {label}
      </summary>
      <ul className="mt-1 ml-2 flex flex-col gap-1 border-l pl-3">
        {items.map((item) => {
          const isCurrent = item.href === currentHref;
          return (
            <li key={item.canonicalUrl}>
              <Link
                aria-current={isCurrent ? "page" : undefined}
                className={cn(
                  "block py-1 text-sm text-muted-foreground hover:text-foreground",
                  isCurrent && "font-medium text-foreground",
                )}
                href={item.href as Route}
                onClick={onNavigate}
              >
                {item.label}
              </Link>
            </li>
          );
        })}
      </ul>
    </details>
  );
}
