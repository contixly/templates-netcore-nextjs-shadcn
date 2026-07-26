"use client";

import { useTranslations } from "next-intl";

import type { ApiFailure } from "@/src/lib/api/result";

export function AuthApiFailure({ failure }: Readonly<{ failure: ApiFailure }>) {
  const t = useTranslations("auth.login.failure");

  return (
    <section className="space-y-2" role="alert">
      <h2 className="text-lg font-semibold">{t("title")}</h2>
      <p className="text-sm text-muted-foreground">{t("description")}</p>
      {failure.kind === "problem" && failure.traceId ? (
        <p className="font-mono text-xs text-muted-foreground">
          {failure.traceId}
        </p>
      ) : null}
    </section>
  );
}
