"use client";

import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { searchDocumentsSystem } from "@/src/lib/api/generated/sdk.gen";
import type { DocumentSearchResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";

export type SearchDocumentsInput = Readonly<{
  query: string;
  locale: "en" | "ru";
  signal?: AbortSignal;
}>;

export async function searchDocuments(
  input: SearchDocumentsInput,
): Promise<ApiResult<DocumentSearchResponse>> {
  try {
    const result = await searchDocumentsSystem({
      client: createBrowserApiClient(),
      query: { q: input.query, locale: input.locale },
      signal: input.signal,
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
