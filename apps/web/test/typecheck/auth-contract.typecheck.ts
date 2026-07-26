import type {
  CreateLocalAutomationScenarioErrors,
  DeleteLocalAutomationScenarioErrors,
  LocalAutomationSignInRequest,
  LogoutErrors,
  ProblemDetails,
  SignInLocalAutomationErrors,
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
