import { getTranslations } from "next-intl/server";

export default async function Loading() {
  const boundaries = await getTranslations("application.shell.safeBoundaries");

  return (
    <main
      aria-busy="true"
      aria-labelledby="application-loading-title"
      className="mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 py-12"
      role="status"
    >
      <h1 className="sr-only" id="application-loading-title">
        {boundaries("loadingTitle")}
      </h1>
      <div className="h-8 w-52 animate-pulse bg-muted" />
      <div className="grid gap-4 md:grid-cols-3">
        {Array.from({ length: 3 }, (_, index) => (
          <div className="space-y-4 border border-border p-6" key={index}>
            <div className="h-4 w-24 animate-pulse bg-muted" />
            <div className="h-10 w-32 animate-pulse bg-muted" />
          </div>
        ))}
      </div>
      <div className="space-y-4 border border-border p-6">
        <div className="h-5 w-40 animate-pulse bg-muted" />
        <div className="h-56 w-full animate-pulse bg-muted" />
      </div>
    </main>
  );
}
