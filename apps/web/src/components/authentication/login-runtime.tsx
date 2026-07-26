import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { AuthApiFailure } from "@/src/components/authentication/auth-api-failure";
import { LocalAutomationLoginPanel } from "@/src/components/authentication/local-automation-login-panel";
import { sanitizeAuthRedirect } from "@/src/features/authentication/sanitize-auth-redirect";
import { loadServerAuthState } from "@/src/lib/api/auth/server/load-server-auth-state";

export async function LoginRuntime({
  searchParams,
}: Readonly<{
  searchParams: Promise<{ redirect?: string | string[] }>;
}>) {
  await connection();
  const redirectPath = sanitizeAuthRedirect((await searchParams).redirect);
  const result = await loadServerAuthState();
  const t = await getTranslations("auth.login");

  if (!result.ok) {
    return <AuthApiFailure failure={result.failure} />;
  }
  if (result.data.session.authenticated) {
    redirect(redirectPath);
  }

  return (
    <section className="w-full max-w-md space-y-6">
      <div className="space-y-2">
        <p className="text-xs font-medium tracking-[0.2em] text-muted-foreground uppercase">
          {t("eyebrow")}
        </p>
        <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-sm text-muted-foreground">{t("description")}</p>
      </div>
      {result.data.capabilities.localAutomationEnabled ? (
        <LocalAutomationLoginPanel redirectPath={redirectPath} />
      ) : (
        <p className="text-sm text-muted-foreground">{t("unavailable")}</p>
      )}
    </section>
  );
}
