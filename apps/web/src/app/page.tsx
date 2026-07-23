import { getTranslations } from "next-intl/server";

export default async function HomePage() {
  const t = await getTranslations("system.page");

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-12">
      <section className="max-w-2xl space-y-3">
        <p className="text-xs font-medium tracking-[0.2em] text-muted-foreground uppercase">
          {t("eyebrow")}
        </p>
        <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-sm leading-6 text-muted-foreground">
          {t("description")}
        </p>
      </section>
    </main>
  );
}
