import type { ReactNode } from "react";

export default function SimpleLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  return children;
}
