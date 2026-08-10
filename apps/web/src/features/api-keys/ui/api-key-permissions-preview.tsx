"use client";

import { useTranslations } from "next-intl";

import { Badge } from "@/src/components/ui/badge";
import type { ApiKeyResponse } from "@/src/lib/api/generated/types.gen";

export function ApiKeyPermissionsPreview({
  emptyLabel,
  scopes,
}: Readonly<{
  emptyLabel: string;
  scopes: readonly ApiKeyResponse["scopes"][number][];
}>) {
  const t = useTranslations("apiKeys.scopes");

  if (scopes.length === 0) {
    return <span className="text-xs text-muted-foreground">{emptyLabel}</span>;
  }

  return (
    <div className="flex flex-wrap gap-1.5">
      {scopes.map((scope) => (
        <Badge key={scope} variant="outline">
          {t(scope)}
        </Badge>
      ))}
    </div>
  );
}
