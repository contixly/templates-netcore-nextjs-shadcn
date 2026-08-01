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
