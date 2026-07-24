import { getTranslations } from "next-intl/server";

export default async function LoginLoading() {
  const t = await getTranslations("auth.login");
  return (
    <main className="grid min-h-screen place-items-center px-4 py-12">
      <p role="status">{t("loading")}</p>
    </main>
  );
}
