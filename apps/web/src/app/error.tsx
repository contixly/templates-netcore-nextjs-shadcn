"use client";

import { useTranslations } from "next-intl";

import { Button } from "@/src/components/ui/button";

export type RouteErrorProps = Readonly<{
  as?: "main" | "section";
  error: Error & { digest?: string };
  reset: () => void;
}>;

export default function RouteError({ as = "main", reset }: RouteErrorProps) {
  const boundaries = useTranslations("application.shell.safeBoundaries");
  const actions = useTranslations("common.actions");
  const content = (
    <>
      <h1 className="text-2xl font-semibold">{boundaries("errorTitle")}</h1>
      <p className="text-muted-foreground">{boundaries("errorDescription")}</p>
      <Button onClick={reset}>{actions("retry")}</Button>
    </>
  );

  return as === "section" ? (
    <section className="mx-auto max-w-2xl space-y-4 px-4 py-16">
      {content}
    </section>
  ) : (
    <main className="mx-auto max-w-2xl space-y-4 px-4 py-16">{content}</main>
  );
}
