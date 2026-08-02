import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const generatedUtils = resolve(
  process.cwd(),
  "src/lib/api/generated/client/utils.gen.ts",
);
const anchor = `export async function setAuthParams(
  options: Pick<RequestOptions, 'auth' | 'query' | 'security'> & {
    headers: Headers;
  },
): Promise<void> {
`;
const hardening = `  if (
    (options.security?.length ?? 0) > 1 &&
    typeof options.auth === 'string' &&
    options.auth.length > 0
  ) {
    throw new Error(
      'Scalar auth cannot be used with alternative security schemes; use a scheme-selective callback or explicit header.',
    );
  }

`;

const source = await readFile(generatedUtils, "utf8");
const anchorCount = source.split(anchor).length - 1;

if (anchorCount !== 1) {
  throw new Error(
    `Generated auth hardening expected one setAuthParams anchor, found ${anchorCount}.`,
  );
}

const hardened = source.replace(anchor, `${anchor}${hardening}`);

if (hardened.split(hardening).length - 1 !== 1) {
  throw new Error("Generated auth hardening was not applied exactly once.");
}

await writeFile(generatedUtils, hardened);
