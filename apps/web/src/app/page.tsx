import { getTranslations } from "next-intl/server";
import { Suspense } from "react";

import { BrowserSystemStatus } from "@/src/components/system/browser-system-status";
import { ServerSystemStatus } from "@/src/components/system/server-system-status";
import { StatusCardSkeleton } from "@/src/components/system/status-card";

export default async function HomePage() {
  const page = await getTranslations("system.page");
  const status = await getTranslations("system.status");

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-12">
      <section className="max-w-2xl space-y-3">
        <p className="text-xs font-medium tracking-[0.2em] text-muted-foreground uppercase">
          {page("eyebrow")}
        </p>
        <h1 className="text-3xl font-semibold tracking-tight">
          {page("title")}
        </h1>
        <p className="text-sm leading-6 text-muted-foreground">
          {page("description")}
        </p>
      </section>
      <section className="grid gap-4 md:grid-cols-2">
        <Suspense
          fallback={
            <StatusCardSkeleton
              label={status("loading")}
              source="ssr"
              title={status("ssrTitle")}
            />
          }
        >
          <ServerSystemStatus />
        </Suspense>
        <BrowserSystemStatus />
      </section>
    </main>
  );
}
