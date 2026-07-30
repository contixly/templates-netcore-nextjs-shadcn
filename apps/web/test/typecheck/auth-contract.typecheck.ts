import type {
  AccountConnectionResponse,
  AccountResponse,
  AccountSessionResponse,
  AccountSessionsRevocationResponse,
  ChallengeExternalAuthData,
  ChallengeExternalAuthErrors,
  CreateLocalAutomationScenarioErrors,
  DeleteAccountData,
  DeleteAccountErrors,
  DeleteLocalAutomationScenarioErrors,
  DisconnectAccountProviderData,
  DisconnectAccountProviderErrors,
  ExternalAuthChallengeRequest,
  ExternalAuthIntent,
  GetAccountSessionsData,
  GetAccountSessionsErrors,
  LocalAutomationSignInRequest,
  LogoutErrors,
  ProblemDetails,
  RevokeAccountSessionData,
  RevokeAccountSessionErrors,
  RevokeOtherAccountSessionsData,
  SignInLocalAutomationErrors,
  UpdateAccountProfileData,
  UpdateProfileRequest,
} from "@/src/lib/api/generated";

const credentials: LocalAutomationSignInRequest = {
  email: "local-agent+typecheck@local-agent.test",
  password: "local-typecheck-password",
};
void credentials;

// @ts-expect-error Both credentials are required by the runtime contract.
const missingEmail: LocalAutomationSignInRequest = {
  password: "local-typecheck-password",
};
void missingEmail;

const nullEmail: LocalAutomationSignInRequest = {
  // @ts-expect-error Credentials cannot be null.
  email: null,
  password: "local-typecheck-password",
};
void nullEmail;

// @ts-expect-error Both credentials are required by the runtime contract.
const missingPassword: LocalAutomationSignInRequest = {
  email: "local-agent+typecheck@local-agent.test",
};
void missingPassword;

const problem: ProblemDetails = {
  type: "urn:template:problem:antiforgery_failed",
  title: "Antiforgery validation failed",
  status: 400,
  detail: "The request antiforgery token is missing or invalid.",
  instance: "/api/local-auth/sign-in",
  code: "antiforgery_failed",
  traceId: "trace-typecheck",
};

const logoutBadRequest: LogoutErrors[400] = problem;
const cleanupBadRequest: DeleteLocalAutomationScenarioErrors[400] = problem;
const createBadRequest: CreateLocalAutomationScenarioErrors[400] = problem;
const signInBadRequest: SignInLocalAutomationErrors[400] = problem;
void logoutBadRequest;
void cleanupBadRequest;
void createBadRequest;
void signInBadRequest;

const provider = "google" as const;
const intent: ExternalAuthIntent = "signIn";
const challengeRequest: ExternalAuthChallengeRequest = {
  intent,
  returnUrl: "/dashboard",
};
const challengeData: ChallengeExternalAuthData = {
  url: "/api/v1/auth/external/{provider}/challenge",
  body: challengeRequest,
  path: { provider },
  headers: { "X-CSRF-TOKEN": "csrf-typecheck" },
};
void challengeData;

const challengeWithoutCsrf: ChallengeExternalAuthData = {
  url: "/api/v1/auth/external/{provider}/challenge",
  body: challengeRequest,
  path: { provider },
  // @ts-expect-error External challenge always requires the antiforgery header.
  headers: {},
};
void challengeWithoutCsrf;

const unknownIntent: ExternalAuthChallengeRequest = {
  // @ts-expect-error The wire intent is a closed signIn/connect enum.
  intent: "register",
  returnUrl: null,
};
void unknownIntent;

const profileRequest: UpdateProfileRequest = {
  displayName: "Typecheck Account",
};
const profileData: UpdateAccountProfileData = {
  url: "/api/v1/account/profile",
  body: profileRequest,
  headers: { "X-CSRF-TOKEN": "csrf-typecheck" },
};
void profileData;

const disconnectData: DisconnectAccountProviderData = {
  url: "/api/v1/account/connections/{provider}",
  path: { provider: "github" },
  headers: { "X-CSRF-TOKEN": "csrf-typecheck" },
};
void disconnectData;

const sessionsData: GetAccountSessionsData = {
  url: "/api/v1/account/sessions",
  query: {
    cursor: "opaque-cursor",
    limit: 20,
  },
};
void sessionsData;

const revokeData: RevokeAccountSessionData = {
  url: "/api/v1/account/sessions/{sessionId}",
  path: { sessionId: "0198776b-6210-7e54-82d8-19fb35683550" },
  headers: { "X-CSRF-TOKEN": "csrf-typecheck" },
};
void revokeData;

const revokeOthersData: RevokeOtherAccountSessionsData = {
  url: "/api/v1/account/sessions/others",
  headers: { "X-CSRF-TOKEN": "csrf-typecheck" },
};
void revokeOthersData;
const revokedSessions: AccountSessionsRevocationResponse = {
  revokedCount: 2,
};
void revokedSessions;
const invalidRevokedSessions: AccountSessionsRevocationResponse = {
  // @ts-expect-error The JSON response is always an int32 number, never a string.
  revokedCount: "2",
};
void invalidRevokedSessions;

const deleteData: DeleteAccountData = {
  url: "/api/v1/account",
  body: { confirmationEmail: "owner@example.test" },
  headers: { "X-CSRF-TOKEN": "csrf-typecheck" },
};
void deleteData;

const account: AccountResponse = {
  id: "0198776b-6210-7e54-82d8-19fb35683550",
  displayName: "Typecheck Account",
  primaryEmail: "owner@example.test",
  imageUrl: null,
  createdAt: "2026-07-29T12:00:00Z",
  verifiedEmails: [
    {
      email: "owner@example.test",
      isPrimary: true,
      providers: ["google"],
    },
  ],
};
void account;

const connection: AccountConnectionResponse = {
  provider: "google",
  displayName: "Google",
  configured: true,
  connected: true,
  email: "owner@example.test",
  connectedAt: "2026-07-29T12:00:00Z",
  lastUsedAt: null,
  isCurrentAuthenticationMethod: false,
  canConnect: false,
  canDisconnect: true,
  disabledReason: null,
};
void connection;

const session: AccountSessionResponse = {
  id: "0198776b-6210-7e54-82d8-19fb35683550",
  createdAt: "2026-07-29T12:00:00Z",
  lastSeenAt: "2026-07-29T12:01:00Z",
  expiresAt: "2026-08-12T12:00:00Z",
  isCurrent: true,
  authenticationMethod: "local",
  ipAddress: "203.0.113.0/24",
  userAgent: null,
};
void session;

const concurrencyProblem: ProblemDetails = {
  ...problem,
  type: "urn:template:problem:concurrency_conflict",
  code: "concurrency_conflict",
};
const challengeRateLimit: ChallengeExternalAuthErrors[429] = problem;
const disconnectConflict: DisconnectAccountProviderErrors[409] =
  concurrencyProblem;
const revokeConflict: RevokeAccountSessionErrors[409] = problem;
const sessionsBadRequest: GetAccountSessionsErrors[400] = problem;
const deleteBadRequest: DeleteAccountErrors[400] = problem;
void challengeRateLimit;
void disconnectConflict;
void revokeConflict;
void sessionsBadRequest;
void deleteBadRequest;
