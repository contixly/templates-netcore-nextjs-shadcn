"use client";

import RouteError, { type RouteErrorProps } from "@/src/app/error";

export default function ProtectedRouteError(props: RouteErrorProps) {
  return <RouteError {...props} as="section" />;
}
