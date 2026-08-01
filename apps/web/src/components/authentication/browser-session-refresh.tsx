"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";

import { refreshBrowserAuthSession } from "@/src/lib/api/auth/browser/refresh-browser-auth-session";

const refreshStartedMarker = Symbol.for(
  "template.browser-session-refresh.started",
);

type BrowserSessionRefreshCycle = Readonly<{
  pathname: string;
}>;

export function BrowserSessionRefresh() {
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    const refreshDocument = document as unknown as Document &
      Record<symbol, BrowserSessionRefreshCycle | undefined>;
    if (pathname === "/dashboard") {
      delete refreshDocument[refreshStartedMarker];
      return;
    }

    if (refreshDocument[refreshStartedMarker]?.pathname === pathname) {
      return;
    }
    const cycle = { pathname };
    refreshDocument[refreshStartedMarker] = cycle;
    void refreshBrowserAuthSession().then((result) => {
      if (refreshDocument[refreshStartedMarker] !== cycle) {
        return;
      }
      if (!result.ok) {
        delete refreshDocument[refreshStartedMarker];
        return;
      }
      router.refresh();
    });
  }, [pathname, router]);

  return null;
}
