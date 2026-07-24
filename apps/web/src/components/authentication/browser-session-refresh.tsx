"use client";

import { useEffect } from "react";

import { refreshBrowserAuthSession } from "@/src/lib/api/auth/browser/refresh-browser-auth-session";

export function BrowserSessionRefresh() {
  useEffect(() => {
    void refreshBrowserAuthSession();
  }, []);

  return null;
}
