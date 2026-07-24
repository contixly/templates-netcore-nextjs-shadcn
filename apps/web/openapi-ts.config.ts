import { defineConfig } from "@hey-api/openapi-ts";

export default defineConfig({
  input: "../../contracts/openapi/v1.json",
  output: {
    path: "src/lib/api/generated",
    clean: true,
  },
  plugins: [
    "@hey-api/typescript",
    "@hey-api/client-fetch",
    {
      name: "@hey-api/sdk",
      operations: {
        strategy: "flat",
      },
    },
  ],
});
