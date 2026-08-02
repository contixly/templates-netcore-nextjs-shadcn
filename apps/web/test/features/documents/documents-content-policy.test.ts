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

  it("states the exact organization contract corrections in both locales", () => {
    const createSwitchEn = readFileSync(
      resolve(contentRoot, "workspace/create-switch.en.md"),
      "utf8",
    );
    const createSwitchRu = readFileSync(
      resolve(contentRoot, "workspace/create-switch.ru.md"),
      "utf8",
    );
    const settingsEn = readFileSync(
      resolve(contentRoot, "workspace/settings.en.md"),
      "utf8",
    );
    const settingsRu = readFileSync(
      resolve(contentRoot, "workspace/settings.ru.md"),
      "utf8",
    );
    const domainsEn = readFileSync(
      resolve(contentRoot, "workspace/email-domains.en.md"),
      "utf8",
    );
    const domainsRu = readFileSync(
      resolve(contentRoot, "workspace/email-domains.ru.md"),
      "utf8",
    );
    const membersEn = readFileSync(
      resolve(contentRoot, "workspace/members-roles.en.md"),
      "utf8",
    );
    const membersRu = readFileSync(
      resolve(contentRoot, "workspace/members-roles.ru.md"),
      "utf8",
    );

    expect(createSwitchEn).toContain("exact current slug");
    expect(createSwitchRu).toContain("точный текущий slug");
    expect(`${createSwitchEn}\n${createSwitchRu}`).not.toMatch(
      /old slug|стар(?:ый|ого) slug/iu,
    );
    for (const deletionText of [
      createSwitchEn,
      createSwitchRu,
      settingsEn,
      settingsRu,
    ]) {
      expect(deletionText).toContain("last_organization_required");
    }
    expect(domainsEn).toContain(
      "at most 100 submitted raw entries before normalization",
    );
    expect(domainsRu).toContain(
      "не более 100 исходных элементов до нормализации",
    );
    expect(membersEn).toContain("Admins and owners can manage API keys");
    expect(membersRu).toContain("Admin и owner могут управлять API-ключами");
  });
});
