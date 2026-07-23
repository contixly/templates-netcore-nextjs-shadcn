import { readdir, readFile } from "node:fs/promises";
import { resolve, relative } from "node:path";
import { spawnSync } from "node:child_process";

const webRoot = process.cwd();
const generatedRoot = resolve(webRoot, "src/lib/api/generated");

async function snapshot(directory) {
  const entries = new Map();

  async function visit(current) {
    const children = await readdir(current, { withFileTypes: true });

    for (const child of children.sort((left, right) =>
      left.name.localeCompare(right.name),
    )) {
      const path = resolve(current, child.name);

      if (child.isDirectory()) {
        await visit(path);
      } else if (child.isFile()) {
        entries.set(
          relative(directory, path),
          (await readFile(path)).toString("base64"),
        );
      }
    }
  }

  await visit(directory);
  return JSON.stringify([...entries]);
}

const before = await snapshot(generatedRoot);
const npm = process.platform === "win32" ? "npm.cmd" : "npm";
const generation = spawnSync(npm, ["run", "api:generate"], {
  cwd: webRoot,
  stdio: "inherit",
});

if (generation.status !== 0) {
  process.exit(generation.status ?? 1);
}

const after = await snapshot(generatedRoot);

if (before !== after) {
  console.error(
    "Generated REST client drifted. Inspect and commit the regenerated tree.",
  );
  process.exit(1);
}

console.log("Generated REST client is deterministic and current.");
