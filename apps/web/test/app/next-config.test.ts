import nextConfig from "@/next.config";

jest.mock("next-intl/plugin", () => ({
  __esModule: true,
  default: () => (config: unknown) => config,
}));

it("enables the Next runtime authorization interrupts used by forbidden()", () => {
  expect(nextConfig).toMatchObject({
    experimental: {
      authInterrupts: true,
    },
  });
});

it("defines the permanent canonical redirect from /docs/index to /docs", async () => {
  expect(nextConfig.redirects).toBeDefined();
  const redirects = await nextConfig.redirects?.();
  expect(redirects).toContainEqual({
    source: "/docs/index",
    destination: "/docs",
    permanent: true,
  });
});
