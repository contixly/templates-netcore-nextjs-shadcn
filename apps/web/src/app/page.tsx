import { getTranslations } from "next-intl/server";

export default async function HomePage() {
  const t = await getTranslations("system.page");

  return (
    <main>
      <p>{t("eyebrow")}</p>
      <h1>{t("title")}</h1>
      <p>{t("description")}</p>
    </main>
  );
}
