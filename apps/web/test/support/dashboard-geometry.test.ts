import { mockDashboardGeometry } from "@/test/support/dashboard-geometry";

it("scopes dashboard geometry and restores the original DOM implementation", () => {
  const original = HTMLElement.prototype.getBoundingClientRect;
  const restore = mockDashboardGeometry();

  expect(document.createElement("div").getBoundingClientRect().width).toBe(320);
  restore();
  expect(HTMLElement.prototype.getBoundingClientRect).toBe(original);
});
