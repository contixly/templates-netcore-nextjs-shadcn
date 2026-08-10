import { getTranslations } from "next-intl/server";
import { Suspense } from "react";

import {
  LoginRuntime,
  LoginRuntimeLoading,
} from "@/src/features/authentication/ui/login-runtime";
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
    <main className="flex min-h-screen items-center justify-center px-6 py-12 md:px-10">
      <Suspense fallback={<LoginRuntimeLoading label={t("loading")} />}>
        <LoginRuntime searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
