import "server-only";

import { getTranslations } from "next-intl/server";
import { connection } from "next/server";

import { BrowserSessionRefresh } from "@/src/components/authentication/browser-session-refresh";
import { ApplicationSidebar } from "@/src/features/application/ui/application-sidebar";
import { loadApplicationShell } from "@/src/lib/api/application/server/load-application-shell";

export type ApplicationNavigationSlotProps = Readonly<{
  redirectPath: string;
  organizationKey?: string;
}>;

export async function ApplicationNavigationSlot({
  redirectPath,
  organizationKey,
}: ApplicationNavigationSlotProps) {
  await connection();
  const shell = await loadApplicationShell(redirectPath, organizationKey);

  if (!shell.ok) {
    const t = await getTranslations("application.shell.safeBoundaries");
    const traceId =
      shell.failure.kind === "problem" ? shell.failure.traceId : undefined;

    return (
      <section role="alert">
        <h2>{t("errorTitle")}</h2>
        <p>{t("errorDescription")}</p>
        {traceId ? <p className="font-mono text-xs">{traceId}</p> : null}
      </section>
    );
  }

  return (
    <>
      <BrowserSessionRefresh />
      <ApplicationSidebar data={shell.data} pathname={redirectPath} />
    </>
  );
}
