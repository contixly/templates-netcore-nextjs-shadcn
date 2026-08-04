import type {
  AccountResponse,
  AuthSessionMetadataResponse,
  AuthUserResponse,
  OrganizationDetailResponse,
  OrganizationSummaryResponse,
} from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";

export type ApplicationShellData = Readonly<{
  account: AccountResponse;
  organizations: readonly Extract<
    OrganizationSummaryResponse,
    { accessPrincipal: "user" }
  >[];
  nextOrganizationCursor: string | null;
  session: AuthSessionMetadataResponse;
  user: AuthUserResponse;
  currentOrganization: OrganizationDetailResponse | null;
}>;

export type ApplicationShellResult = ApiResult<ApplicationShellData>;
