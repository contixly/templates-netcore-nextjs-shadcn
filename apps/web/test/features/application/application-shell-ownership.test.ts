/** @jest-environment node */

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const source = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

test("keeps the simple route layout limited to feature composition", () => {
  const layout = source("src/app/(simple)/layout.tsx");

  expect(layout).toContain(
    'import { SimpleApplicationShell } from "@/src/features/application/ui/simple-application-shell";',
  );
  expect(layout).toContain("<SimpleApplicationShell>");
  for (const presentationDetail of [
    "<header",
    "IconBooks",
    "IconHome",
    "Button",
    "ThemeSwitcher",
    "getTranslations",
  ]) {
    expect(layout).not.toContain(presentationDetail);
  }
});

test.each([
  [
    "protected application header",
    "src/features/application/ui/application-header.tsx",
  ],
  [
    "public landing header",
    "src/features/application/ui/landing/landing-page.tsx",
  ],
])("keeps the %s flow-relative until the medium breakpoint", (_, path) => {
  const component = source(path);

  expect(component).toContain("md:sticky md:top-0");
  expect(component).not.toContain('className="sticky top-0');
});
