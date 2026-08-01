import "server-only";

import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getTeamMemberCandidates } from "@/src/lib/api/generated/sdk.gen";
import type {
  GetTeamMemberCandidatesData,
  TeamCandidatePageResponse,
} from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

export type LoadTeamMemberCandidatesQuery = Readonly<
  NonNullable<GetTeamMemberCandidatesData["query"]>
>;

export async function loadTeamMemberCandidates(
  organizationId: string,
  teamId: string,
  query: LoadTeamMemberCandidatesQuery = {},
): Promise<ApiResult<TeamCandidatePageResponse>> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  try {
    const result = await getTeamMemberCandidates({
      client: client.client,
      cache: "no-store",
      headers: { "X-Template-Session-Renewal": "suppress" },
      path: { organizationId, teamId },
      query,
    });
    return result.data !== undefined
      ? { ok: true, data: result.data.data }
      : {
          ok: false,
          failure: normalizeApiFailure(result.error, result.response),
        };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}
