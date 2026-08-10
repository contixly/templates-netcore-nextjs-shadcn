import { fileURLToPath } from "node:url";

const visualParityTest = "ui-reference-parity.spec.ts";

const desktopLight = Object.freeze({
  colorScheme: "light",
  device: "desktop",
  name: "desktop-light",
});

export function selectPlaywrightRuntime({ canonical, live }) {
  if (typeof canonical !== "boolean" || typeof live !== "boolean") {
    throw new Error("Runtime selection requires canonical and live booleans.");
  }

  if (live) {
    return {
      behavioralProjectNames: [],
      mode: "live-provider",
      projects: [
        {
          ...desktopLight,
          testMatch: "external-provider-smoke.spec.ts",
        },
      ],
      visualParityProjectNames: [],
      visualServerIds: [],
    };
  }

  if (!canonical) {
    return {
      behavioralProjectNames: [desktopLight.name],
      mode: "portable",
      projects: [{ ...desktopLight, testIgnore: visualParityTest }],
      visualParityProjectNames: [],
      visualServerIds: [],
    };
  }

  const projects = [
    desktopLight,
    {
      colorScheme: "dark",
      device: "desktop",
      name: "desktop-dark",
      testMatch: visualParityTest,
    },
    {
      colorScheme: "light",
      device: "mobile",
      name: "mobile-light",
      testMatch: visualParityTest,
    },
    {
      colorScheme: "dark",
      device: "mobile",
      name: "mobile-dark",
      testMatch: visualParityTest,
    },
  ];
  return {
    behavioralProjectNames: [desktopLight.name],
    mode: "canonical",
    projects,
    visualParityProjectNames: projects.map(({ name }) => name),
    visualServerIds: ["russian", "mobile", "mobile-russian"],
  };
}

function main(arguments_) {
  if (arguments_[0] !== "--evaluate" || !arguments_[1]) {
    throw new Error("Expected --evaluate <json>.");
  }
  process.stdout.write(
    `${JSON.stringify(selectPlaywrightRuntime(JSON.parse(arguments_[1])))}\n`,
  );
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2));
}
