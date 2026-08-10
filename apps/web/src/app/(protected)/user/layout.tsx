import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";
import { Suspense, type ReactNode } from "react";

import { AccountNav } from "@/src/components/account/account-nav";
import {
  SettingsContentRail,
  SettingsPageShell,
} from "@/src/features/application/ui/settings/settings-shell";
import { AuthApiFailure } from "@/src/components/authentication/auth-api-failure";
import { LogoutButton } from "@/src/components/authentication/logout-button";
import { accountRoutes } from "@/src/features/account/account-routes";
import { authLoginUrl } from "@/src/features/authentication/sanitize-auth-redirect";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";

export async function AuthenticatedAccountShell({
  children,
}: Readonly<{ children: ReactNode }>) {
  await connection();
  const result = await loadServerAuthSession();

  if (!result.ok) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <AuthApiFailure failure={result.failure} />
      </div>
    );
  }
  if (result.data.authenticated === false) {
    redirect(authLoginUrl(accountRoutes.profile));
  }
  if (
    result.data.authenticated !== true ||
    !result.data.user ||
    !result.data.session
  ) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-12">
        <AuthApiFailure
          failure={{ kind: "network", code: "api_unavailable" }}
        />
      </div>
    );
  }

  return (
    <SettingsPageShell>
      <div className="w-full shrink-0 md:w-56">
        <AccountNav />
        <div className="border-b p-2 md:border-r">
          <LogoutButton />
        </div>
      </div>
      <SettingsContentRail>{children}</SettingsContentRail>
    </SettingsPageShell>
  );
}

export default async function UserLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  const t = await getTranslations("account.navigation");

  return (
    <Suspense
      fallback={
        <p className="mx-auto w-full max-w-5xl px-4 py-12" role="status">
          {t("loading")}
        </p>
      }
    >
      <AuthenticatedAccountShell>{children}</AuthenticatedAccountShell>
    </Suspense>
  );
}
