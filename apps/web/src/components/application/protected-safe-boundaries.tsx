import Link from "next/link";
import { getTranslations } from "next-intl/server";

import { Button } from "@/src/components/ui/button";
import { applicationRoutes } from "@/src/features/application/application-routes";

export async function ProtectedNotFound() {
  const boundaries = await getTranslations("application.shell.safeBoundaries");
  const actions = await getTranslations("common.actions");

  return (
    <section className="mx-auto max-w-2xl space-y-4 px-4 py-16">
      <h1 className="text-2xl font-semibold">{boundaries("notFoundTitle")}</h1>
      <p className="text-muted-foreground">
        {boundaries("notFoundDescription")}
      </p>
      <Button asChild>
        <Link href={applicationRoutes.home}>{actions("home")}</Link>
      </Button>
    </section>
  );
}

export async function ProtectedForbidden() {
  const boundaries = await getTranslations("application.shell.safeBoundaries");
  const actions = await getTranslations("common.actions");

  return (
    <section className="mx-auto max-w-2xl space-y-4 px-4 py-16">
      <h1 className="text-2xl font-semibold">{boundaries("forbiddenTitle")}</h1>
      <p className="text-muted-foreground">
        {boundaries("forbiddenDescription")}
      </p>
      <Button asChild>
        <Link href={applicationRoutes.home}>{actions("home")}</Link>
      </Button>
    </section>
  );
}

export async function ProtectedUnauthorized() {
  const boundaries = await getTranslations("application.shell.safeBoundaries");

  return (
    <section className="mx-auto max-w-2xl space-y-4 px-4 py-16">
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
    </section>
  );
}
