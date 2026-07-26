import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import { AuthApiFailure } from "@/src/components/authentication/auth-api-failure";
import { BrowserSessionRefresh } from "@/src/components/authentication/browser-session-refresh";
import { LogoutButton } from "@/src/components/authentication/logout-button";
import { Card, CardContent } from "@/src/components/ui/card";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { authLoginUrl } from "@/src/features/authentication/sanitize-auth-redirect";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";

export async function DashboardRuntime() {
  await connection();
  const result = await loadServerAuthSession();
  const t = await getTranslations("auth.dashboard");

  if (!result.ok) {
    return <AuthApiFailure failure={result.failure} />;
  }
  if (result.data.authenticated === false) {
    redirect(authLoginUrl(authenticationRoutes.dashboard));
  }
  if (
    result.data.authenticated !== true ||
    !result.data.user ||
    !result.data.session
  ) {
    return (
      <AuthApiFailure failure={{ kind: "network", code: "api_unavailable" }} />
    );
  }

  return (
    <section className="mx-auto w-full max-w-3xl space-y-6 px-4 py-12">
      <BrowserSessionRefresh />
      <div className="space-y-2">
        <p className="text-xs font-medium tracking-[0.2em] text-muted-foreground uppercase">
          {t("eyebrow")}
        </p>
        <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-sm text-muted-foreground">{t("description")}</p>
      </div>
      <Card>
        <CardContent className="space-y-4">
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            <dt className="text-muted-foreground">{t("name")}</dt>
            <dd>{result.data.user.name}</dd>
            <dt className="text-muted-foreground">{t("email")}</dt>
            <dd>{result.data.user.email}</dd>
            <dt className="text-muted-foreground">{t("emailVerified")}</dt>
            <dd>{result.data.user.emailVerified ? t("yes") : t("no")}</dd>
            <dt className="text-muted-foreground">{t("sessionId")}</dt>
            <dd className="font-mono" data-testid="session-id">
              {result.data.session.id}
            </dd>
            <dt className="text-muted-foreground">{t("expiresAt")}</dt>
            <dd>
              <time dateTime={result.data.session.expiresAt}>
                {result.data.session.expiresAt}
              </time>
            </dd>
          </dl>
          <LogoutButton />
        </CardContent>
      </Card>
    </section>
  );
}
