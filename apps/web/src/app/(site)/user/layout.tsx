import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";
import { Suspense, type ReactNode } from "react";

import { AccountNav } from "@/src/components/account/account-nav";
import { AuthApiFailure } from "@/src/components/authentication/auth-api-failure";
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
      <main className="mx-auto w-full max-w-5xl px-4 py-12">
        <AuthApiFailure failure={result.failure} />
      </main>
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
      <main className="mx-auto w-full max-w-5xl px-4 py-12">
        <AuthApiFailure
          failure={{ kind: "network", code: "api_unavailable" }}
        />
      </main>
    );
  }

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-1 flex-col md:flex-row">
      <AccountNav />
      <main className="min-w-0 flex-1 px-4 py-8 md:px-6">{children}</main>
    </div>
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
