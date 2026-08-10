"use client";

import { useTranslations } from "next-intl";
import { IconAlertTriangle } from "@tabler/icons-react";

import { Alert, AlertDescription, AlertTitle } from "@/src/components/ui/alert";
import type { ApiFailure } from "@/src/lib/api/result";

export function AuthApiFailure({ failure }: Readonly<{ failure: ApiFailure }>) {
  const t = useTranslations("auth.login.failure");

  return (
    <Alert className="mx-auto max-w-md" variant="destructive">
      <IconAlertTriangle aria-hidden="true" />
      <AlertTitle>
        <h2>{t("title")}</h2>
      </AlertTitle>
      <AlertDescription>
        <p>{t("description")}</p>
        {failure.kind === "problem" && failure.traceId ? (
          <p className="font-mono text-xs">{failure.traceId}</p>
        ) : null}
      </AlertDescription>
    </Alert>
  );
}
