import type { ReactNode } from "react";
import { cookies } from "next/headers";

import { ProtectedApplicationShell } from "@/src/components/application/protected-application-shell";
import { parseSidebarPreference } from "@/src/components/application/sidebar-state";

export default async function ProtectedLayout({
  children,
  applicationNavigation,
}: Readonly<{
  children: ReactNode;
  applicationNavigation: ReactNode;
}>) {
  const cookieStore = await cookies();
  const defaultSidebarOpen = parseSidebarPreference(cookieStore.toString());

  return (
    <ProtectedApplicationShell
      defaultSidebarOpen={defaultSidebarOpen}
      navigation={applicationNavigation}
    >
      {children}
    </ProtectedApplicationShell>
  );
}
