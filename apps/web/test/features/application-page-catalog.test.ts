import {
  applicationPageCatalog,
  resolveApplicationPage,
} from "@/src/features/application/application-page-catalog";

describe("applicationPageCatalog", () => {
  it.each([
    ["/", "home", true],
    ["/dashboard", "dashboard", false],
    ["/user/security", "accountSecurity", false],
    ["/w/acme/dashboard", "organizationDashboard", false],
    ["/w/acme/settings/teams", "organizationTeams", false],
  ])("resolves %s to %s", (pathname, id, indexable) => {
    expect(resolveApplicationPage(pathname)).toMatchObject({ id, indexable });
  });

  it("keeps catalog IDs unique", () => {
    const ids = applicationPageCatalog.map(({ id }) => id);

    expect(new Set(ids).size).toBe(ids.length);
  });
});
