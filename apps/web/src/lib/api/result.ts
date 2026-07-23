import type { ApiConfigurationCode } from "@/src/lib/api/api-base-url";
import type { SystemStatusResponse } from "@/src/lib/api/generated";

export type ApiFailure =
  | {
      kind: "problem";
      code: string;
      status: number;
      traceId?: string;
    }
  | {
      kind: "network";
      code: "api_unavailable";
    }
  | {
      kind: "configuration";
      code: ApiConfigurationCode;
    };

export type ApiResult<T> =
  { ok: true; data: T } | { ok: false; failure: ApiFailure };

export type SystemStatusResult = ApiResult<SystemStatusResponse>;
