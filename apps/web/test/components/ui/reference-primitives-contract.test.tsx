/* eslint-disable @typescript-eslint/no-require-imports */
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const globals = readFileSync(
  resolve(process.cwd(), "src/app/globals.css"),
  "utf8",
);

test("exposes the reference semantic color, font, chart and motion tokens", () => {
  for (const token of [
    "--popover:",
    "--popover-foreground:",
    "--chart-1:",
    "--chart-5:",
    "--transition-ease:",
    "--font-sans:",
  ])
    expect(globals).toContain(token);
  expect(globals).toContain('@plugin "@tailwindcss/typography"');
  expect(globals).toContain("@media (prefers-reduced-motion: reduce)");
});

test("ships every reference primitive required by migrated surfaces", () => {
  for (const file of [
    "command",
    "input-group",
    "item",
    "kbd",
    "spinner",
    "scroll-area",
  ]) {
    expect(() =>
      require(resolve(process.cwd(), `src/components/ui/${file}`)),
    ).not.toThrow();
  }
});
