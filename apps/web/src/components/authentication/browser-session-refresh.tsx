"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { refreshBrowserAuthSession } from "@/src/lib/api/auth/browser/refresh-browser-auth-session";

export function BrowserSessionRefresh() {
  const router = useRouter();

  useEffect(() => {
    void refreshBrowserAuthSession().then((result) => {
      if (result.ok) {
        router.refresh();
      }
    });
  }, [router]);

  return null;
}
