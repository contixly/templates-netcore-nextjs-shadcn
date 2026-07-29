import { getTranslations } from "next-intl/server";

import { DeleteAccountDialog } from "@/src/components/account/delete-account-dialog";
import { loadAccount } from "@/src/lib/api/account/server/load-account";
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

export default async function DangerPage() {
  const [page, danger, failure, result] = await Promise.all([
    getTranslations("account.pages.danger"),
    getTranslations("account.danger"),
    getTranslations("account.failure"),
    loadAccount(),
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
        <h1 className="text-2xl font-semibold text-destructive">
          {page("title")}
        </h1>
        <p className="text-sm text-muted-foreground">{page("description")}</p>
      </header>
      <section
        className="flex flex-col gap-4 border border-destructive/40 bg-destructive/5 p-4"
        aria-labelledby="delete-account-heading"
      >
        <div className="flex flex-col gap-1">
          <h2
            className="text-sm font-semibold text-destructive"
            id="delete-account-heading"
          >
            {danger("title")}
          </h2>
          <p className="text-sm text-muted-foreground">
            {danger("description")}
          </p>
          <p className="text-sm font-medium text-destructive">
            {danger("warning")}
          </p>
        </div>
        <DeleteAccountDialog primaryEmail={result.data.primaryEmail} />
      </section>
    </article>
  );
}
