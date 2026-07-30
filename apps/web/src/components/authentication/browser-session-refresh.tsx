"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";

import { refreshBrowserAuthSession } from "@/src/lib/api/auth/browser/refresh-browser-auth-session";

const refreshStartedMarker = Symbol.for(
  "template.browser-session-refresh.started",
);

export function BrowserSessionRefresh() {
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    if (pathname === "/dashboard") {
      return;
    }

    const refreshWindow = window as unknown as Window &
      Record<symbol, boolean | undefined>;
    if (refreshWindow[refreshStartedMarker]) {
      return;
    }
    refreshWindow[refreshStartedMarker] = true;
    void refreshBrowserAuthSession().then((result) => {
      if (result.ok) {
        router.refresh();
      }
    });
  }, [pathname, router]);

  return null;
}
