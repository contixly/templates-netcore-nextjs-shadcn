import { IconArrowLeft, IconArrowRight } from "@tabler/icons-react";
import type { Route } from "next";
import Link from "next/link";
import { useTranslations } from "next-intl";

import type { DocumentPageNavigation } from "@/src/features/documents/documents-types";

export function DocumentsPageNavigation({
  navigation,
  placement = "bottom",
}: Readonly<{
  navigation?: DocumentPageNavigation;
  placement?: "top" | "bottom";
}>) {
  const t = useTranslations("documents.page");

  if (!navigation?.previous && !navigation?.next) return null;

  return (
    <nav
      aria-label={t("navigation")}
      className={
        placement === "top"
          ? "mb-8 grid gap-4 border-b pb-6 sm:grid-cols-2"
          : "mt-12 grid gap-4 border-t pt-6 sm:grid-cols-2"
      }
    >
      {navigation.previous ? (
        <Link
          aria-label={`${t("previous")}: ${navigation.previous.title}`}
          className="flex flex-col gap-1 text-sm hover:text-foreground"
          href={navigation.previous.href as Route}
        >
          <span className="flex items-center gap-1 text-xs text-muted-foreground">
            <IconArrowLeft aria-hidden="true" />
            {t("previous")}
          </span>
          <span className="font-medium">{navigation.previous.title}</span>
        </Link>
      ) : (
        <span aria-hidden="true" />
      )}
      {navigation.next ? (
        <Link
          aria-label={`${t("next")}: ${navigation.next.title}`}
          className="flex flex-col gap-1 text-left text-sm hover:text-foreground sm:text-right"
          href={navigation.next.href as Route}
        >
          <span className="flex items-center gap-1 text-xs text-muted-foreground sm:justify-end">
            {t("next")}
            <IconArrowRight aria-hidden="true" />
          </span>
          <span className="font-medium">{navigation.next.title}</span>
        </Link>
      ) : null}
    </nav>
  );
}
