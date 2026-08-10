import { render, screen } from "@testing-library/react";
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { relative, resolve } from "node:path";
import type { ReactNode } from "react";

import DashboardLoading from "@/src/app/(protected)/dashboard/loading";
import InvitationLoading from "@/src/app/(protected)/invite/[invitationId]/loading";
import SettingsLoading from "@/src/app/(protected)/w/[organizationKey]/settings/loading";
import WorkspacesLoading from "@/src/app/(protected)/workspaces/loading";
import RouteError from "@/src/app/error";
import { ProtectedApplicationShell } from "@/src/features/application/ui/protected-application-shell";
import ProtectedRouteError from "@/src/features/application/ui/protected-route-error";
import {
  ProtectedForbidden,
  ProtectedNotFound,
  ProtectedUnauthorized,
} from "@/src/features/application/ui/protected-safe-boundaries";

const webRoot = process.cwd();

jest.mock("@/src/features/application/ui/application-header", () => ({
  ApplicationHeader: () => <header>Application header</header>,
}));
jest.mock("next-intl", () => ({
  useTranslations: () => (key: string) => key,
}));
jest.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
}));
jest.mock("@/src/components/ui/sidebar", () => ({
  SidebarInset: ({ children }: { children: ReactNode }) => (
    <div data-testid="sidebar-inset">{children}</div>
  ),
  SidebarProvider: ({ children }: { children: ReactNode }) => (
    <div data-testid="sidebar-provider">{children}</div>
  ),
}));

function sourceFiles(directory: string): string[] {
  return execFileSync("find", [directory, "-type", "f", "-name", "*.tsx"], {
    encoding: "utf8",
  })
    .trim()
    .split("\n")
    .filter(Boolean);
}

it("reserves main for standalone route surfaces and shared top-level shells", () => {
  const sourceRoot = resolve(webRoot, "src");
  const filesWithMain = sourceFiles(sourceRoot)
    .filter((path) => /<main\b/u.test(readFileSync(path, "utf8")))
    .map((path) => relative(webRoot, path).replaceAll("\\", "/"))
    .sort();

  expect(filesWithMain).toEqual([
    "src/app/(public)/(home)/loading.tsx",
    "src/app/(simple)/auth/error/page.tsx",
    "src/app/(simple)/auth/login/loading.tsx",
    "src/app/(simple)/auth/login/page.tsx",
    "src/app/error.tsx",
    "src/app/forbidden.tsx",
    "src/app/global-error.tsx",
    "src/app/loading.tsx",
    "src/app/not-found.tsx",
    "src/app/unauthorized.tsx",
    "src/features/application/ui/landing/landing-page.tsx",
    "src/features/application/ui/protected-application-shell.tsx",
    "src/features/documents/ui/documents-shell.tsx",
  ]);
});

it("keeps every protected error boundary nested inside the shell landmark", () => {
  const errorFiles = sourceFiles(resolve(webRoot, "src/app/(protected)"))
    .filter((path) => path.endsWith("/error.tsx"))
    .map((path) => ({
      localPath: relative(webRoot, path).replaceAll("\\", "/"),
      source: readFileSync(path, "utf8"),
    }));

  expect(errorFiles.length).toBeGreaterThan(0);
  for (const errorFile of errorFiles) {
    expect(errorFile).toEqual({
      localPath: errorFile.localPath,
      source: expect.stringContaining(
        "@/src/features/application/ui/protected-route-error",
      ),
    });
    expect(errorFile.source).not.toContain('from "@/src/app/error"');
  }
});

it("provides protected not-found and access fallbacks that do not reuse standalone mains", () => {
  for (const file of ["not-found.tsx", "forbidden.tsx", "unauthorized.tsx"]) {
    const path = resolve(webRoot, "src/app/(protected)", file);
    expect({ file, exists: existsSync(path) }).toEqual({ file, exists: true });
    const source = readFileSync(path, "utf8");
    expect({ file, source }).toEqual({
      file,
      source: expect.stringContaining(
        "@/src/features/application/ui/protected-safe-boundaries",
      ),
    });
  }
});

it("renders representative protected loading, error, not-found, and access surfaces with one main and the skip target", async () => {
  const variants = [
    ["success", <section key="success">Protected success</section>],
    ["dashboard loading", await DashboardLoading()],
    ["invite loading", await InvitationLoading()],
    ["settings loading", await SettingsLoading()],
    ["workspaces loading", await WorkspacesLoading()],
    [
      "error",
      <ProtectedRouteError
        error={new Error("private")}
        key="error"
        reset={() => undefined}
      />,
    ],
    ["not found", await ProtectedNotFound()],
    ["forbidden", await ProtectedForbidden()],
    ["unauthorized", await ProtectedUnauthorized()],
  ] as const;

  const view = render(
    <ProtectedApplicationShell navigation={<nav>Navigation</nav>}>
      {variants[0][1]}
    </ProtectedApplicationShell>,
  );

  for (const [variant, surface] of variants) {
    view.rerender(
      <ProtectedApplicationShell navigation={<nav>Navigation</nav>}>
        {surface}
      </ProtectedApplicationShell>,
    );

    expect({ variant, count: screen.getAllByRole("main").length }).toEqual({
      variant,
      count: 1,
    });
    expect(screen.getByRole("main")).toHaveAttribute("id", "main-content");
    expect({
      variant,
      count: document.querySelectorAll("#main-content").length,
    }).toEqual({ variant, count: 1 });
  }
});

it("preserves a standalone main landmark for the root route error", () => {
  render(<RouteError error={new Error("private")} reset={() => undefined} />);

  expect(screen.getAllByRole("main")).toHaveLength(1);
});
