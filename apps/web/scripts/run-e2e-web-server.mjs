import { spawn, spawnSync } from "node:child_process";
import { copyFile, cp, mkdir, rm, symlink } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const replicatedFiles = [
  "next.config.ts",
  "postcss.config.mjs",
  "tsconfig.json",
  "mdx-components.tsx",
  "package.json",
];
const replicatedDirectories = ["public", "src"];

function readOption(arguments_, name) {
  const index = arguments_.indexOf(name);
  return index === -1 ? undefined : arguments_[index + 1];
}

function serverOptions(arguments_) {
  const portValue = readOption(arguments_, "--port");
  const port = Number(portValue);
  const locale = readOption(arguments_, "--locale");
  if (!Number.isInteger(port) || port < 1 || port > 65_535) {
    throw new Error("--port must be an integer between 1 and 65535.");
  }
  if (locale !== "en" && locale !== "ru") {
    throw new Error("--locale must be en or ru.");
  }
  return {
    https: arguments_.includes("--https"),
    locale,
    port,
    root: path.resolve(readOption(arguments_, "--root") ?? process.cwd()),
    temporaryRoot: path.resolve(
      readOption(arguments_, "--temp-root") ?? tmpdir(),
    ),
  };
}

export function buildE2EWebServerPlan(options) {
  const target = path.join(
    options.temporaryRoot,
    `netcore-nextjs-shadcn-e2e-${options.locale}-${options.port}`,
  );
  const keyPath = path.join(target, "localhost-key.pem");
  const certificatePath = path.join(target, "localhost.pem");
  const certificate = options.https
    ? {
        args: [
          "req",
          "-x509",
          "-newkey",
          "rsa:2048",
          "-nodes",
          "-keyout",
          keyPath,
          "-out",
          certificatePath,
          "-days",
          "1",
          "-subj",
          "/CN=127.0.0.1",
          "-addext",
          "subjectAltName=IP:127.0.0.1,DNS:localhost",
        ],
        command: "openssl",
      }
    : null;
  const args = [
    path.join(options.root, "node_modules", "next", "dist", "bin", "next"),
    "dev",
    target,
    "--webpack",
    ...(options.https
      ? [
          "--experimental-https",
          "--experimental-https-key",
          keyPath,
          "--experimental-https-cert",
          certificatePath,
        ]
      : []),
    "--hostname",
    "127.0.0.1",
    "--port",
    String(options.port),
  ];
  return {
    certificate,
    directories: replicatedDirectories,
    files: replicatedFiles,
    locale: options.locale,
    next: { args, command: process.execPath },
    nodeModulesLink: {
      source: path.join(options.root, "node_modules"),
      target: path.join(target, "node_modules"),
    },
    port: options.port,
    root: options.root,
    target,
  };
}

async function prepare(plan) {
  await rm(plan.target, { force: true, recursive: true });
  await mkdir(plan.target, { recursive: true });
  await Promise.all(
    plan.files.map((file) =>
      copyFile(path.join(plan.root, file), path.join(plan.target, file)),
    ),
  );
  await Promise.all(
    plan.directories.map((directory) =>
      cp(path.join(plan.root, directory), path.join(plan.target, directory), {
        recursive: true,
      }),
    ),
  );
  await symlink(
    plan.nodeModulesLink.source,
    plan.nodeModulesLink.target,
    process.platform === "win32" ? "junction" : "dir",
  );
  if (plan.certificate) {
    const result = spawnSync(plan.certificate.command, plan.certificate.args, {
      stdio: "ignore",
    });
    if (result.status !== 0) {
      throw new Error(
        `Local HTTPS certificate generation failed with ${result.status ?? "no exit status"}.`,
      );
    }
  }
}

async function main(arguments_) {
  const plan = buildE2EWebServerPlan(serverOptions(arguments_));
  if (arguments_.includes("--print-plan")) {
    process.stdout.write(`${JSON.stringify(plan)}\n`);
    return;
  }

  await prepare(plan);
  const child = spawn(plan.next.command, plan.next.args, {
    env: process.env,
    stdio: "inherit",
  });
  for (const signal of ["SIGINT", "SIGTERM"]) {
    process.once(signal, () => child.kill(signal));
  }
  child.once("error", (error) => {
    throw error;
  });
  child.once("exit", (code) => {
    process.exitCode = code ?? 1;
  });
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  await main(process.argv.slice(2));
}
