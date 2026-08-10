import { getTranslations } from "next-intl/server";

import { LoginRuntimeLoading } from "@/src/features/authentication/ui/login-runtime";

export default async function LoginLoading() {
  const t = await getTranslations("auth.login");
  return (
    <main className="flex min-h-screen items-center justify-center px-6 py-12 md:px-10">
      <LoginRuntimeLoading label={t("loading")} />
    </main>
  );
}
