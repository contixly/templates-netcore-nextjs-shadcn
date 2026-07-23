"use client";

import { useTranslations } from "next-intl";

import { Button } from "@/src/components/ui/button";

export default function RouteError({
  reset,
}: Readonly<{
  error: Error & { digest?: string };
  reset: () => void;
}>) {
  const boundaries = useTranslations("system.boundaries");
  const actions = useTranslations("common.actions");

  return (
    <main className="mx-auto max-w-2xl space-y-4 px-4 py-16">
      <h1 className="text-2xl font-semibold">{boundaries("routeTitle")}</h1>
      <p className="text-muted-foreground">{boundaries("routeDescription")}</p>
      <Button onClick={reset}>{actions("retry")}</Button>
    </main>
  );
}
