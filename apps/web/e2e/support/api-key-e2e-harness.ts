import {
  expect,
  type ElementHandle,
  type Locator,
  type Page,
  type TestInfo,
} from "@playwright/test";

import type {
  ApiKeyResponse,
  MachineOrganizationDetailResponse,
  OrganizationMemberResponse,
  OrganizationSummaryResponse,
  ProblemDetails,
  TeamMemberResponse,
  TeamResponse,
} from "../../src/lib/api/generated";
import { waitForInteraction } from "./app-readiness";
import type {
  GeneratedApiCall,
  GeneratedCreatedApiKey,
} from "./generated-api-keys-api";
import type { OrganizationTestIdentity } from "./organization-test-fixture";

const problemCopy = {
  api_key_invalid: {
    detail: "The supplied API key is invalid.",
    title: "API key invalid",
  },
  api_key_missing: {
    detail: "An API key is required to access this resource.",
    title: "API key required",
  },
  api_key_permission_denied: {
    detail: "You do not have permission to perform this API key operation.",
    title: "API key permission denied",
  },
  organization_access_denied: {
    detail: "The API key cannot access the requested organization.",
    title: "Organization access denied",
  },
  team_not_found: {
    detail: "The requested team was not found.",
    title: "Team not found",
  },
} as const;

type ApiKeyProblemCode = keyof typeof problemCopy;

export function uniqueApiKeyIdentity(
  testInfo: TestInfo,
  label: string,
): OrganizationTestIdentity {
  const run = crypto.randomUUID().replaceAll("-", "");
  const safeLabel = label.toLowerCase().replaceAll(/[^a-z0-9]+/gu, "-");
  return {
    email: `local-agent+api-key-${safeLabel}-${run}@local-agent.test`,
    name: `API key ${label} ${testInfo.workerIndex}-${testInfo.retry}`,
    password: `E2E-Api-Key-${run}!A1`,
  };
}

function assertResponseMetadata(
  call: GeneratedApiCall<unknown>,
  contentType: "application/json" | "application/problem+json",
) {
  expect(call.cacheControl).toBe("no-store");
  expect(call.contentType).toBe(contentType);
  expect(call.hasSetCookie).toBe(false);
}

export function assertGeneratedSuccess<T>(
  call: GeneratedApiCall<T>,
  status: number,
): T {
  expect(call.status).toBe(status);
  expect(call.ok).toBe(true);
  assertResponseMetadata(call, "application/json");
  expect(call.envelopeKeys).toEqual(["data"]);
  expect(call.problemKeys).toEqual([]);
  if (!call.ok) {
    throw new Error(
      `Generated API request failed with ${call.status} (${call.problem?.code ?? "unknown"}).`,
    );
  }
  return call.data;
}

export function assertApiKeyProblem(
  call: GeneratedApiCall<unknown>,
  expected: Readonly<{
    code: ApiKeyProblemCode;
    credential?: string;
    instance: string;
    status: number;
  }>,
) {
  expect(call.status).toBe(expected.status);
  expect(call.ok).toBe(false);
  assertResponseMetadata(call, "application/problem+json");
  expect(call.envelopeKeys).toEqual([]);
  expect(call.problemKeys).toEqual([
    "code",
    "detail",
    "instance",
    "status",
    "title",
    "traceId",
    "type",
  ]);
  if (call.ok || !call.problem) {
    throw new Error(
      `Generated API request did not return the expected sanitized Problem Details (${expected.code}).`,
    );
  }

  const problem: ProblemDetails = call.problem;
  if (expected.credential) {
    assertNoCredentialEcho(problem, expected.credential);
  }
  expect(problem.status).toBe(expected.status);
  expect(problem.code).toBe(expected.code);
  expect(problem.type).toBe(`urn:template:problem:${expected.code}`);
  expect(problem.title).toBe(problemCopy[expected.code].title);
  expect(problem.detail).toBe(problemCopy[expected.code].detail);
  expect(problem.instance).toBe(expected.instance);
  expect(Boolean(problem.traceId.trim())).toBe(true);
}

function containsCredential(
  value: unknown,
  credential: string,
  seen: WeakSet<object>,
): boolean {
  if (!credential) return false;
  if (typeof value === "string") return value.includes(credential);
  if (typeof value !== "object" || value === null) return false;
  if (seen.has(value)) return false;
  seen.add(value);
  return Object.values(value).some((entry) =>
    containsCredential(entry, credential, seen),
  );
}

export function assertNoCredentialEcho(value: unknown, credential: string) {
  if (containsCredential(value, credential, new WeakSet())) {
    throw new Error("Generated API response echoed a reveal-once credential.");
  }
}

export function assertSafeApiKey(apiKey: ApiKeyResponse) {
  expect(Object.keys(apiKey).sort()).toEqual([
    "createdAt",
    "enabled",
    "expiresAt",
    "id",
    "lastRequestAt",
    "name",
    "ownerId",
    "ownerKind",
    "rateLimitEnabled",
    "rateLimitMax",
    "rateLimitWindow",
    "requestCount",
    "rotatedAt",
    "scopes",
    "start",
    "status",
    "updatedAt",
    "windowStartedAt",
  ]);
  const unsafe = Object.keys(apiKey).some((name) =>
    /credential|hash|key$/iu.test(name),
  );
  if (unsafe) {
    throw new Error("Safe API-key projection contained a secret field.");
  }
}

export function assertGeneratedApiKeyCreated(
  created: GeneratedCreatedApiKey,
  collectionPath: string,
  credential: string,
) {
  assertNoCredentialEcho(created.apiKey, credential);
  expect(Object.keys(created).sort()).toEqual([
    "apiKey",
    "response",
    "takeCredential",
  ]);
  const apiKey = assertGeneratedSuccess(created.response, 201);
  expect(apiKey.id).toBe(created.apiKey.id);
  expect(created.response.location).toBe(`${collectionPath}/${apiKey.id}`);
  assertSafeApiKey(apiKey);
}

const capabilityKeys = [
  "canAddMembers",
  "canDeleteOrganization",
  "canManageApiKeys",
  "canManageInvitations",
  "canManageTeams",
  "canUpdateMemberRoles",
  "canUpdateOrganization",
] as const;

const organizationProjectionKeys = [
  "accessPrincipal",
  "canonicalKey",
  "capabilities",
  "createdAt",
  "currentRole",
  "id",
  "name",
  "slug",
  "updatedAt",
] as const;

export function assertUserOrganizationSummary(
  organization: MachineOrganizationDetailResponse | OrganizationSummaryResponse,
  options: Readonly<{
    id?: string;
    role: "admin" | "member" | "owner";
  }>,
) {
  expect(Object.keys(organization).sort()).toEqual(organizationProjectionKeys);
  expect(Object.keys(organization.capabilities).sort()).toEqual(capabilityKeys);
  expect(organization.accessPrincipal).toBe("user");
  expect(organization.currentRole).toBe(options.role);
  if (options.id) expect(organization.id).toBe(options.id);
  const capabilities =
    options.role === "owner"
      ? {
          canAddMembers: true,
          canDeleteOrganization: true,
          canManageApiKeys: true,
          canManageInvitations: true,
          canManageTeams: true,
          canUpdateMemberRoles: true,
          canUpdateOrganization: true,
        }
      : options.role === "admin"
        ? {
            canAddMembers: true,
            canDeleteOrganization: false,
            canManageApiKeys: true,
            canManageInvitations: true,
            canManageTeams: true,
            canUpdateMemberRoles: true,
            canUpdateOrganization: true,
          }
        : {
            canAddMembers: false,
            canDeleteOrganization: false,
            canManageApiKeys: false,
            canManageInvitations: false,
            canManageTeams: false,
            canUpdateMemberRoles: false,
            canUpdateOrganization: false,
          };
  expect(organization.capabilities).toEqual(capabilities);
}

export function assertOrganizationOwnedSummary(
  organization: MachineOrganizationDetailResponse | OrganizationSummaryResponse,
  organizationId: string,
) {
  expect(Object.keys(organization).sort()).toEqual(organizationProjectionKeys);
  expect(Object.keys(organization.capabilities).sort()).toEqual(capabilityKeys);
  expect(organization.id).toBe(organizationId);
  expect(organization.accessPrincipal).toBe("organization");
  expect(organization.currentRole).toBe("organization");
  expect(organization.capabilities).toEqual({
    canAddMembers: false,
    canDeleteOrganization: false,
    canManageApiKeys: false,
    canManageInvitations: false,
    canManageTeams: false,
    canUpdateMemberRoles: false,
    canUpdateOrganization: false,
  });
}

export function assertOrganizationMemberShape(
  member: OrganizationMemberResponse,
) {
  expect(Object.keys(member).sort()).toEqual([
    "email",
    "emailDomain",
    "id",
    "imageUrl",
    "isOutsideAllowedEmailDomains",
    "joinedAt",
    "name",
    "role",
    "userId",
  ]);
}

export function assertTeamShape(
  team: TeamResponse,
  options: Readonly<{
    memberCount?: number;
    membersIncluded: boolean;
    organizationId: string;
  }>,
) {
  expect(Object.keys(team).sort()).toEqual([
    "createdAt",
    "id",
    "memberCount",
    "members",
    "membersIncluded",
    "name",
    "organizationId",
    "updatedAt",
  ]);
  expect(Object.keys(team.members).sort()).toEqual(["items", "nextCursor"]);
  expect(team.organizationId).toBe(options.organizationId);
  expect(team.membersIncluded).toBe(options.membersIncluded);
  if (options.memberCount !== undefined) {
    expect(team.memberCount).toBe(options.memberCount);
  }
}

export function assertTeamMemberShape(member: TeamMemberResponse) {
  expect(Object.keys(member).sort()).toEqual([
    "email",
    "id",
    "imageUrl",
    "name",
    "organizationJoinedAt",
    "role",
    "teamJoinedAt",
    "userId",
  ]);
}

export function assertOpaquePageContinuation(
  page: Readonly<{ nextCursor: null | string }>,
): string {
  expect(Object.keys(page).sort()).toEqual(["items", "nextCursor"]);
  if (
    typeof page.nextCursor !== "string" ||
    !/^[A-Za-z0-9_-]+$/u.test(page.nextCursor)
  ) {
    throw new Error("Generated page did not return an opaque continuation.");
  }
  return page.nextCursor;
}

type GeneratedPage<T extends Readonly<{ id: string }>> = Readonly<{
  items: Array<T>;
  nextCursor: null | string;
}>;

export async function collectGeneratedPagesToExhaustion<
  T extends Readonly<{ id: string }>,
>(
  options: Readonly<{
    expectedIds: readonly string[];
    fetchPage: (
      cursor: string | undefined,
    ) => Promise<GeneratedApiCall<GeneratedPage<T>>>;
    validateItem: (item: T) => void;
    validatePage?: (page: GeneratedPage<T>) => void;
  }>,
): Promise<readonly T[]> {
  const collected: T[] = [];
  const seenCursors = new Set<string>();
  const expectedPageCount = options.expectedIds.length;
  let cursor: string | undefined;
  let fetchedPageCount = 0;
  let reachedTerminalPage = false;

  for (let pageIndex = 0; pageIndex < expectedPageCount; pageIndex += 1) {
    const page = assertGeneratedSuccess(await options.fetchPage(cursor), 200);
    fetchedPageCount += 1;
    expect(Object.keys(page).sort()).toEqual(["items", "nextCursor"]);
    options.validatePage?.(page);
    if (page.items.length !== 1) {
      throw new Error(
        "Generated limit=1 page did not contain exactly one item.",
      );
    }
    for (const item of page.items) {
      options.validateItem(item);
      collected.push(item);
    }

    const isExpectedTerminalPage = pageIndex === expectedPageCount - 1;
    if (page.nextCursor !== null) {
      const nextCursor = assertOpaquePageContinuation(page);
      if (seenCursors.has(nextCursor)) {
        throw new Error(
          "Generated pagination repeated an opaque continuation.",
        );
      }
      seenCursors.add(nextCursor);
      cursor = nextCursor;
    }

    if ((page.nextCursor === null) !== isExpectedTerminalPage) {
      throw new Error(
        "Generated pagination did not terminate after the exact expected page count.",
      );
    }
    reachedTerminalPage = page.nextCursor === null;
  }

  if (!reachedTerminalPage || fetchedPageCount !== expectedPageCount) {
    throw new Error(
      "Generated pagination did not terminate after the exact expected page count.",
    );
  }

  const collectedIds = collected.map((item) => item.id);
  expect(new Set(collectedIds).size).toBe(collectedIds.length);
  expect(new Set(collectedIds)).toEqual(new Set(options.expectedIds));
  expect(collectedIds).toEqual(options.expectedIds);
  return collected;
}

export async function credentialCodeNodeIsRemovedOrCleared<TNode extends Node>(
  credentialNode: ElementHandle<TNode>,
): Promise<boolean> {
  return credentialNode.evaluate(
    (node) => !node.isConnected || (node.textContent ?? "").trim().length === 0,
  );
}

function apiKeyRow(page: Page, name: string): Locator {
  return page
    .getByRole("row")
    .filter({ has: page.getByText(name, { exact: true }) });
}

export async function expectApiKeyRow(
  page: Page,
  name: string,
  status: "Active" | "Disabled" | "Expired",
) {
  const row = apiKeyRow(page, name);
  await expect(row).toHaveCount(1);
  await expect(row).toContainText(status);
  return row;
}

async function captureRevealOnceCredential(
  page: Page,
  ownerKind: "organization" | "user",
): Promise<string> {
  const dialog = page.getByRole("dialog", {
    name: "Save this API key now",
  });
  await dialog.waitFor({ state: "visible" });
  const credentialView = dialog.getByRole("code");
  if ((await credentialView.count()) !== 1) {
    throw new Error("Reveal-once API credential view was unavailable.");
  }
  const credentialNode = await credentialView.elementHandle();
  if (!credentialNode) {
    throw new Error("Reveal-once API credential node was unavailable.");
  }
  let credential = (await credentialView.textContent())?.trim() ?? "";
  try {
    const prefix = ownerKind === "organization" ? "org" : "user";
    const valid = new RegExp(`^${prefix}_[A-Za-z0-9_-]{43}$`, "u").test(
      credential,
    );

    const close = dialog.getByRole("button", { name: "I saved it" });
    await close.click();
    await dialog.waitFor({ state: "hidden" });
    await expect
      .poll(() => credentialCodeNodeIsRemovedOrCleared(credentialNode), {
        message: "Reveal-once API credential node was not removed or cleared.",
      })
      .toBe(true);
    if (!valid) {
      throw new Error("Reveal-once API credential had an invalid safe format.");
    }
    return credential;
  } finally {
    credential = "";
  }
}

async function expectUiMutationResponse(
  responsePromise: Promise<
    Readonly<{
      headers(): Record<string, string>;
      status(): number;
    }>
  >,
  expectedStatus: number,
) {
  const response = await responsePromise;
  const headers = response.headers();
  if (response.status() !== expectedStatus) {
    throw new Error(
      `UI API-key mutation failed with ${response.status()} (expected ${expectedStatus}).`,
    );
  }
  if (
    headers["cache-control"] !== "no-store" ||
    headers["content-type"]?.split(";", 1)[0] !== "application/json" ||
    headers["set-cookie"] !== undefined
  ) {
    throw new Error("UI API-key mutation returned unsafe response metadata.");
  }
  return response;
}

export async function createApiKeyThroughUi(
  page: Page,
  options: Readonly<{
    name: string;
    ownerKind: "organization" | "user";
  }>,
): Promise<string> {
  const open = page.getByRole("button", { name: "Create API key" });
  await waitForInteraction(open);
  await open.click();
  const dialog = page.getByRole("dialog", { name: "Create API key" });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel("Name", { exact: true }).fill(options.name);
  const submit = dialog.getByRole("button", {
    name: "Create API key",
    exact: true,
  });
  await expect(submit).toBeEnabled();
  const response = page.waitForResponse((candidate) => {
    const request = candidate.request();
    const url = new URL(candidate.url());
    return request.method() === "POST" && url.pathname.endsWith("/api-keys");
  });
  await submit.click();
  const createResponse = await expectUiMutationResponse(response, 201);
  if (
    !/^\/api\/v1\/(?:account|organizations\/[0-9a-f-]{36})\/api-keys\/[0-9a-f-]{36}$/u.test(
      createResponse.headers().location ?? "",
    )
  ) {
    throw new Error("UI API-key create returned an invalid safe location.");
  }
  let credential = "";
  try {
    credential = await captureRevealOnceCredential(page, options.ownerKind);
    await expect(
      page.getByText("API key created.", { exact: true }),
    ).toBeVisible();
    return credential;
  } finally {
    credential = "";
  }
}

export async function editApiKeyNameThroughUi(
  page: Page,
  currentName: string,
  nextName: string,
) {
  const row = await expectApiKeyRow(page, currentName, "Active");
  const open = row.getByRole("button", { name: "Edit", exact: true });
  await waitForInteraction(open);
  await open.click();
  const dialog = page.getByRole("dialog", { name: `Edit ${currentName}` });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel("Name", { exact: true }).fill(nextName);
  const save = dialog.getByRole("button", { name: "Save changes" });
  await expect(save).toBeEnabled();
  const response = page.waitForResponse((candidate) => {
    const request = candidate.request();
    const url = new URL(candidate.url());
    return (
      request.method() === "PATCH" &&
      /\/api-keys\/[0-9a-f-]{36}$/u.test(url.pathname)
    );
  });
  await save.click();
  await expectUiMutationResponse(response, 200);
  await expect(dialog).toBeHidden();
  await expect(
    page.getByText("API key updated.", { exact: true }),
  ).toBeVisible();
}

export async function toggleApiKeyThroughUi(
  page: Page,
  name: string,
  action: "Disable" | "Enable",
) {
  const row = apiKeyRow(page, name);
  await expect(row).toHaveCount(1);
  const toggle = row.getByRole("button", { name: action, exact: true });
  await waitForInteraction(toggle);
  const response = page.waitForResponse((candidate) => {
    const request = candidate.request();
    const url = new URL(candidate.url());
    return (
      request.method() === "PATCH" &&
      /\/api-keys\/[0-9a-f-]{36}$/u.test(url.pathname)
    );
  });
  await toggle.click();
  await expectUiMutationResponse(response, 200);
  await expect(
    page.getByText(`API key ${action.toLowerCase()}d.`, { exact: true }),
  ).toBeVisible();
}

export async function rotateApiKeyThroughUi(
  page: Page,
  name: string,
  ownerKind: "organization" | "user",
): Promise<string> {
  const row = apiKeyRow(page, name);
  await expect(row).toHaveCount(1);
  const open = row.getByRole("button", { name: "Rotate", exact: true });
  await waitForInteraction(open);
  await open.click();
  const dialog = page.getByRole("dialog", { name: `Rotate ${name}?` });
  await expect(dialog).toBeVisible();
  const confirm = dialog.getByRole("button", { name: "Rotate key" });
  await expect(confirm).toBeEnabled();
  const response = page.waitForResponse((candidate) => {
    const request = candidate.request();
    const url = new URL(candidate.url());
    return (
      request.method() === "POST" &&
      /\/api-keys\/[0-9a-f-]{36}\/rotate$/u.test(url.pathname)
    );
  });
  await confirm.click();
  await expectUiMutationResponse(response, 200);
  let credential = "";
  try {
    credential = await captureRevealOnceCredential(page, ownerKind);
    await expect(
      page.getByText("API key rotated.", { exact: true }),
    ).toBeVisible();
    return credential;
  } finally {
    credential = "";
  }
}

export async function revokeApiKeyThroughUi(page: Page, name: string) {
  const row = apiKeyRow(page, name);
  await expect(row).toHaveCount(1);
  const open = row.getByRole("button", { name: "Revoke", exact: true });
  await waitForInteraction(open);
  await open.click();
  const dialog = page.getByRole("dialog", { name: `Revoke ${name}?` });
  await expect(dialog).toBeVisible();
  const confirm = dialog.getByRole("button", { name: "Revoke key" });
  await expect(confirm).toBeEnabled();
  const response = page.waitForResponse((candidate) => {
    const request = candidate.request();
    const url = new URL(candidate.url());
    return (
      request.method() === "DELETE" &&
      /\/api-keys\/[0-9a-f-]{36}$/u.test(url.pathname)
    );
  });
  await confirm.click();
  await expectUiMutationResponse(response, 200);
  await expect(dialog).toBeHidden();
  await expect(
    page.getByText("API key revoked.", { exact: true }),
  ).toBeVisible();
}
