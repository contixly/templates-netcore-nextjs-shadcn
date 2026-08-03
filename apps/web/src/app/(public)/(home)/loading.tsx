import { Skeleton } from "@/src/components/ui/skeleton";

export default function HomeLoading() {
  return (
    <div className="flex min-h-screen min-w-0 flex-col bg-background">
      <header className="border-b">
        <div className="mx-auto flex h-14 max-w-6xl items-center justify-between px-4 sm:px-6 lg:px-8">
          <Skeleton className="h-6 w-36" />
          <Skeleton className="h-8 w-40" />
        </div>
      </header>
      <main aria-busy="true" className="flex-1">
        <section
          aria-hidden="true"
          className="border-b px-4 py-20 sm:px-6 md:py-28 lg:px-8"
        >
          <div className="mx-auto flex max-w-5xl flex-col gap-6">
            <Skeleton className="h-4 w-52" />
            <Skeleton className="h-24 w-full max-w-3xl" />
            <Skeleton className="h-14 w-full max-w-2xl" />
            <Skeleton className="h-9 w-full max-w-64" />
          </div>
        </section>
        <section
          aria-hidden="true"
          className="px-4 py-16 sm:px-6 md:py-24 lg:px-8"
        >
          <div className="mx-auto max-w-5xl">
            <div className="grid gap-4 border-b pb-10 md:grid-cols-2 md:gap-12">
              <Skeleton className="h-16 w-full" />
              <Skeleton className="h-16 w-full" />
            </div>
            <div className="grid gap-px sm:grid-cols-2">
              {Array.from({ length: 4 }, (_, index) => (
                <div className="bg-background p-6 sm:p-8" key={index}>
                  <Skeleton className="h-28 w-full" />
                </div>
              ))}
            </div>
          </div>
        </section>
      </main>
    </div>
  );
}
