"use client";

import { useCallback } from "react";

import { useSidebar } from "@/src/components/ui/sidebar";

export function useMobileSidebarClose() {
  const { isMobile, setOpenMobile } = useSidebar();

  return useCallback(() => {
    if (isMobile) {
      setOpenMobile(false);
    }
  }, [isMobile, setOpenMobile]);
}
