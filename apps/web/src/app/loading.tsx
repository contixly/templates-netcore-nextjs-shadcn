import { getTranslations } from "next-intl/server";

import { StatusCardSkeleton } from "@/src/components/system/status-card";

export default async function Loading() {
  const boundaries = await getTranslations("system.boundaries");
  const status = await getTranslations("system.status");

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-12">
      <h1 className="text-2xl font-semibold">{boundaries("loading")}</h1>
      <section className="grid gap-4 md:grid-cols-2">
        <StatusCardSkeleton
          label={status("loading")}
          source="ssr"
          title={status("ssrTitle")}
        />
        <StatusCardSkeleton
          label={status("loading")}
          source="browser"
          title={status("browserTitle")}
        />
      </section>
    </main>
  );
}
