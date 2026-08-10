import type { Metadata } from "next";
import type { ReactNode } from "react";
import { cookies } from "next/headers";

import { ProtectedApplicationShell } from "@/src/features/application/ui/protected-application-shell";
import { parseSidebarPreference } from "@/src/features/application/ui/sidebar-state";

export const metadata: Metadata = {
  robots: { index: false, follow: false },
  alternates: { canonical: null },
  openGraph: { url: null },
};

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
