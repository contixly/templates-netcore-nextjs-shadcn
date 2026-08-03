import { getTranslations } from "next-intl/server";
import { Suspense } from "react";

import { LoginRuntime } from "@/src/components/authentication/login-runtime";
import { buildApplicationPageMetadata } from "@/src/lib/metadata";

export function generateMetadata() {
  return buildApplicationPageMetadata("login");
}

export default async function LoginPage({
  searchParams,
}: Readonly<{
  searchParams: Promise<{ redirect?: string | string[] }>;
}>) {
  const t = await getTranslations("auth.login");
  return (
    <main className="grid min-h-screen place-items-center px-4 py-12">
      <Suspense fallback={<p role="status">{t("loading")}</p>}>
        <LoginRuntime searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
