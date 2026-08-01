"use client";

import { useEffect } from "react";

export const APP_HYDRATED_ATTRIBUTE = "data-app-hydrated";

export function AppHydrationMarker() {
  useEffect(() => {
    document.documentElement.setAttribute(APP_HYDRATED_ATTRIBUTE, "true");

    return () => {
      document.documentElement.removeAttribute(APP_HYDRATED_ATTRIBUTE);
    };
  }, []);

  return null;
}
