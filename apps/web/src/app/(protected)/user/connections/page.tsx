import { getTranslations } from "next-intl/server";

import { ConnectionsList } from "@/src/components/account/connections-list";
import { loadConnections } from "@/src/lib/api/account/server/load-connections";
import type { ApiFailure } from "@/src/lib/api/result";

function Failure({
  description,
  failure,
  title,
}: Readonly<{
  description: string;
  failure: ApiFailure;
  title: string;
}>) {
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

export default async function ConnectionsPage() {
  const [page, failure, result] = await Promise.all([
    getTranslations("account.pages.connections"),
    getTranslations("account.failure"),
    loadConnections(),
  ]);

  if (!result.ok) {
    return (
      <Failure
        description={failure("description")}
        failure={result.failure}
        title={failure("title")}
      />
    );
  }

  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{page("title")}</h1>
        <p className="text-sm text-muted-foreground">{page("description")}</p>
      </header>
      <ConnectionsList initialConnections={result.data} />
    </article>
  );
}
