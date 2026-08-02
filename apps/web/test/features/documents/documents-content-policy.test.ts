import { readFileSync, readdirSync } from "node:fs";
import { resolve } from "node:path";

const contentRoot = resolve(process.cwd(), "src/features/documents/content");

function readSection(section: "account" | "workspace", locale: "en" | "ru") {
  return readdirSync(resolve(contentRoot, section))
    .filter((fileName) => fileName.endsWith(`.${locale}.md`))
    .sort()
    .map((fileName) =>
      readFileSync(resolve(contentRoot, section, fileName), "utf8"),
    )
    .join("\n");
}

describe("account and workspace documentation content policy", () => {
  const accountEn = readSection("account", "en");
  const accountRu = readSection("account", "ru");
  const workspaceEn = readSection("workspace", "en");
  const workspaceRu = readSection("workspace", "ru");
  const allCurrentText = [accountEn, accountRu, workspaceEn, workspaceRu].join(
    "\n",
  );

  it("documents the ASP.NET Core REST target in both locales", () => {
    expect(accountEn).toContain("ASP.NET Core");
    expect(accountEn).toContain("HttpOnly");
    expect(accountRu).toContain("ASP.NET Core");
    expect(workspaceEn).toContain("/api/v1/organizations");
    expect(workspaceRu).toContain("/api/v1/organizations");
  });

  it("does not prescribe superseded implementation patterns", () => {
    expect(allCurrentText).not.toMatch(
      /use Prisma directly|call a Server Action|Better Auth owns/iu,
    );
  });
});
