import Link from "next/link";
import { getTranslations } from "next-intl/server";

import { Button } from "@/src/components/ui/button";
import { applicationRoutes } from "@/src/features/application/application-routes";
import { authenticationRoutes } from "@/src/features/authentication/authentication-routes";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

const errorMessageKeys = {
  already_authenticated: "alreadyAuthenticated",
  external_auth_failed: "externalAuthFailed",
  external_email_conflict: "externalEmailConflict",
  external_email_required: "externalEmailRequired",
  external_email_unverified: "externalEmailUnverified",
  external_identity_conflict: "externalIdentityConflict",
  external_provider_not_configured: "externalProviderNotConfigured",
  invalid_return_url: "invalidReturnUrl",
  oauth_flow_context_changed: "oauthFlowContextChanged",
} as const;

type ErrorMessageKey =
  (typeof errorMessageKeys)[keyof typeof errorMessageKeys] | "generic";

function errorMessageKey(
  value: string | string[] | undefined,
): ErrorMessageKey {
  if (typeof value !== "string" || !Object.hasOwn(errorMessageKeys, value)) {
    return "generic";
  }

  return errorMessageKeys[value as keyof typeof errorMessageKeys];
}

export function generateMetadata() {
  return buildApplicationPageMetadata("authError");
}

export default async function AuthErrorPage({
  searchParams,
}: Readonly<{
  searchParams: Promise<{ code?: string | string[] }>;
}>) {
  const t = await getTranslations("auth.error");
  const messageKey = errorMessageKey((await searchParams).code);

  return (
    <main className="grid min-h-screen place-items-center px-4 py-12">
      <section className="w-full max-w-md space-y-6">
        <div className="space-y-2">
          <p className="text-xs font-medium tracking-[0.2em] text-muted-foreground uppercase">
            {t("eyebrow")}
          </p>
          <h1 className="text-3xl font-semibold tracking-tight">
            {t(`codes.${messageKey}.title`)}
          </h1>
          <p className="text-sm text-muted-foreground">
            {t(`codes.${messageKey}.description`)}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button asChild>
            <Link href={authenticationRoutes.login}>{t("retry")}</Link>
          </Button>
          <Button asChild variant="outline">
            <Link href={applicationRoutes.home}>{t("home")}</Link>
          </Button>
        </div>
      </section>
    </main>
  );
}
