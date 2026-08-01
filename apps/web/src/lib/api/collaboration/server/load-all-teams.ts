import "server-only";

import { loadTeams } from "@/src/lib/api/collaboration/server/load-teams";
import type { TeamResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";

const teamPageLimit = 100;

export async function loadAllTeams(
  organizationId: string,
): Promise<ApiResult<readonly TeamResponse[]>> {
  const teamsById = new Map<string, TeamResponse>();
  const seenCursors = new Set<string>();
  let cursor: string | undefined;

  while (true) {
    const page = await loadTeams(organizationId, {
      ...(cursor ? { cursor } : {}),
      limit: teamPageLimit,
    });
    if (!page.ok) return page;

    for (const team of page.data.items) teamsById.set(team.id, team);

    const nextCursor = page.data.nextCursor;
    if (nextCursor === null) {
      return { ok: true, data: [...teamsById.values()] };
    }
    if (nextCursor.length === 0 || seenCursors.has(nextCursor)) {
      return {
        ok: false,
        failure: { kind: "network", code: "api_unavailable" },
      };
    }

    seenCursors.add(nextCursor);
    cursor = nextCursor;
  }
}
