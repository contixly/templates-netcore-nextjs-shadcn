import type { Route } from "next";

export type ApiKeyOwner =
  | Readonly<{ kind: "personal" }>
  | Readonly<{
      kind: "organization";
      organizationId: string;
      organizationKey: string;
      capabilities: Readonly<{ canManageApiKeys: boolean }>;
    }>;

export const apiKeyRoutes = {
  personal: "/user/api-keys" as Route,
  organization: (organizationKey: string) =>
    `/w/${organizationKey}/settings/api-keys` as Route,
} as const;
