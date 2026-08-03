import type { ReactNode } from "react";

export default function ProtectedLayout({
  children,
  applicationNavigation,
}: Readonly<{
  children: ReactNode;
  applicationNavigation: ReactNode;
}>) {
  return (
    <>
      {applicationNavigation}
      <main id="main-content">{children}</main>
    </>
  );
}
