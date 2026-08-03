import Link from "next/link";
import { getTranslations } from "next-intl/server";

import { Button } from "@/src/components/ui/button";
import { applicationRoutes } from "@/src/features/application/application-routes";

export default async function Unauthorized() {
  const boundaries = await getTranslations("application.shell.safeBoundaries");

  return (
    <main className="mx-auto max-w-2xl space-y-4 px-4 py-16">
      <h1 className="text-2xl font-semibold">
        {boundaries("unauthorizedTitle")}
      </h1>
      <p className="text-muted-foreground">
        {boundaries("unauthorizedDescription")}
      </p>
      <Button asChild>
        <Link href={applicationRoutes.login}>
          {boundaries("unauthorizedAction")}
        </Link>
      </Button>
    </main>
  );
}
