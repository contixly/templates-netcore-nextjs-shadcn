import { redirect } from "next/navigation";
import { connection } from "next/server";
import { getTranslations } from "next-intl/server";

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/src/components/ui/empty";
import { Skeleton } from "@/src/components/ui/skeleton";
import { AuthApiFailure } from "@/src/features/authentication/ui/auth-api-failure";
import { ExternalProviderButtons } from "@/src/features/authentication/ui/external-provider-buttons";
import { LocalAutomationLoginPanel } from "@/src/features/authentication/ui/local-automation-login-panel";
import { sanitizeAuthRedirect } from "@/src/features/authentication/sanitize-auth-redirect";
import { loadServerAuthState } from "@/src/lib/api/auth/server/load-server-auth-state";

export function LoginRuntimeLoading({ label }: Readonly<{ label: string }>) {
  return (
    <section aria-busy="true" className="w-full max-w-sm" role="status">
      <span className="sr-only">{label}</span>
      <Card className="min-h-96">
        <CardHeader className="items-center text-center">
          <Skeleton className="h-3 w-24" />
          <Skeleton className="h-6 w-32" />
          <Skeleton className="h-4 w-56 max-w-full" />
        </CardHeader>
        <CardContent className="flex flex-1 flex-col justify-center gap-3">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </CardContent>
      </Card>
    </section>
  );
}

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
    <section className="flex w-full max-w-sm flex-col gap-6">
      <Card className="min-h-96">
        <CardHeader className="text-center">
          <p className="text-xs font-medium tracking-[0.2em] text-muted-foreground uppercase">
            {t("eyebrow")}
          </p>
          <CardTitle className="text-xl">
            <h1>{t("title")}</h1>
          </CardTitle>
          <CardDescription>{t("description")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-1 flex-col justify-center">
          {result.data.capabilities.providers.length > 0 ? (
            <ExternalProviderButtons
              providers={result.data.capabilities.providers}
              returnUrl={redirectPath}
            />
          ) : result.data.capabilities.localAutomationEnabled ? null : (
            <Empty>
              <EmptyHeader>
                <EmptyTitle>{t("unavailable")}</EmptyTitle>
                <EmptyDescription>{t("description")}</EmptyDescription>
              </EmptyHeader>
            </Empty>
          )}
        </CardContent>
      </Card>
      {result.data.capabilities.localAutomationEnabled ? (
        <LocalAutomationLoginPanel redirectPath={redirectPath} />
      ) : null}
    </section>
  );
}
