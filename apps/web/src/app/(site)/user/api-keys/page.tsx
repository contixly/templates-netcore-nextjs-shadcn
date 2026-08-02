import { getTranslations } from "next-intl/server";

import { ApiKeyManagement } from "@/src/components/api-keys/api-key-management";
import { loadApiKeys } from "@/src/lib/api/api-keys/server/load-api-keys";
import type { ApiFailure } from "@/src/lib/api/result";

function Failure({
  description,
  failure,
  title,
}: Readonly<{ description: string; failure: ApiFailure; title: string }>) {
  return (
    <section className="flex flex-col gap-2" role="alert">
      <h1 className="text-xl font-semibold">{title}</h1>
      <p className="text-sm text-muted-foreground">{description}</p>
      {failure.kind === "problem" && failure.traceId ? (
        <p className="font-mono text-xs text-muted-foreground">
          {failure.traceId}
        </p>
      ) : null}
    </section>
  );
}

export default async function PersonalApiKeysPage() {
  const [t, result] = await Promise.all([
    getTranslations("apiKeys.page"),
    loadApiKeys({ kind: "personal" }, { limit: 50 }),
  ]);

  if (!result.ok)
    return (
      <Failure
        description={t("failureDescription")}
        failure={result.failure}
        title={t("failureTitle")}
      />
    );

  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{t("title")}</h1>
        <p className="text-sm text-muted-foreground">{t("description")}</p>
      </header>
      <ApiKeyManagement
        initialPage={result.data}
        owner={{ kind: "personal" }}
      />
    </article>
  );
}
