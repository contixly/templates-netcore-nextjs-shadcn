import { getTranslations } from "next-intl/server";

export default async function DocumentsLoading() {
  const t = await getTranslations("documents.boundary");

  return (
    <div role="status" aria-live="polite">
      {t("loading")}
    </div>
  );
}
