import Link from "next/link";
import { getTranslations } from "next-intl/server";

import { Button } from "@/src/components/ui/button";
import { applicationRoutes } from "@/src/features/application/application-routes";

export default async function NotFound() {
  const boundaries = await getTranslations("system.boundaries");
  const actions = await getTranslations("common.actions");

  return (
    <main className="mx-auto max-w-2xl space-y-4 px-4 py-16">
      <h1 className="text-2xl font-semibold">{boundaries("notFoundTitle")}</h1>
      <p className="text-muted-foreground">
        {boundaries("notFoundDescription")}
      </p>
      <Button asChild>
        <Link href={applicationRoutes.home}>{actions("home")}</Link>
      </Button>
    </main>
  );
}
