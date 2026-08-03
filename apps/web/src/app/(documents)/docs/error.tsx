"use client";

import { useTranslations } from "next-intl";

export default function DocumentsError({
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  const t = useTranslations("documents.boundary");

  return (
    <section role="alert">
      <h2>{t("errorTitle")}</h2>
      <p>{t("errorDescription")}</p>
      <button type="button" onClick={reset}>
        {t("retry")}
      </button>
    </section>
  );
}
