import type { ApiConfigurationCode } from "@/src/lib/api/api-base-url";
import type {
  AuthCapabilitiesResponse,
  AuthSessionResponse,
  LocalAutomationScenarioResponse,
  SystemStatusResponse,
} from "@/src/lib/api/generated";

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
export type AuthCapabilitiesResult = ApiResult<AuthCapabilitiesResponse>;
export type AuthSessionResult = ApiResult<AuthSessionResponse>;
export type LocalAutomationScenarioResult =
  ApiResult<LocalAutomationScenarioResponse>;
export type AuthCsrfResult = ApiResult<string>;
