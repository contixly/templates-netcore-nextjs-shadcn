import {
  parseSidebarPreference,
  serializeSidebarPreference,
} from "@/src/components/application/sidebar-state";

describe("sidebar preference", () => {
  it("defaults missing and invalid values to closed", () => {
    expect(parseSidebarPreference()).toBe(false);
    expect(parseSidebarPreference("template.sidebar=open")).toBe(true);
    expect(parseSidebarPreference("template.sidebar=closed")).toBe(false);
    expect(parseSidebarPreference("template.sidebar=invalid")).toBe(false);
    expect(
      parseSidebarPreference("other=value; template.sidebar=open; next=value"),
    ).toBe(true);
  });

  it("serializes a dedicated 30-day same-site preference", () => {
    expect(serializeSidebarPreference(true)).toBe(
      "template.sidebar=open; Path=/; Max-Age=2592000; SameSite=Lax",
    );
    expect(serializeSidebarPreference(false)).toContain(
      "template.sidebar=closed",
    );
  });
});
