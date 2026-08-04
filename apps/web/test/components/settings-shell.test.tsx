import { screen } from "@testing-library/react";

import {
  SettingsContentRail,
  SettingsPageIntro,
  SettingsPageSection,
  SettingsSection,
} from "@/src/components/application/settings/settings-shell";
import { renderWithMessages } from "@/test/support/render";

it("keeps the protected shell as the only main landmark", () => {
  renderWithMessages(
    <main id="main-content">
      <SettingsContentRail>Settings content</SettingsContentRail>
    </main>,
  );

  expect(screen.getAllByRole("main")).toHaveLength(1);
  expect(screen.getByText("Settings content")).toHaveAttribute(
    "data-slot",
    "settings-content-rail",
  );
});

it("provides readable semantic settings sections", () => {
  renderWithMessages(
    <SettingsPageSection mode="readable">
      <SettingsPageIntro
        title="Profile settings"
        description="Review details"
      />
      <SettingsSection title="Display name">Form</SettingsSection>
    </SettingsPageSection>,
  );

  expect(
    screen.getByRole("heading", { level: 1, name: "Profile settings" }),
  ).toBeInTheDocument();
  expect(screen.getByRole("region", { name: "Display name" })).toHaveAttribute(
    "data-variant",
    "default",
  );
  expect(
    screen.getByText("Form").closest('[data-mode="readable"]'),
  ).toHaveClass("max-w-3xl");
});

it("keeps the shared wide rail and destructive section semantics stable", () => {
  renderWithMessages(
    <SettingsPageSection mode="wide">
      <SettingsSection title="Delete account" variant="destructive">
        Irreversible
      </SettingsSection>
    </SettingsPageSection>,
  );

  expect(
    screen.getByText("Irreversible").closest('[data-mode="wide"]'),
  ).toHaveClass("max-w-6xl");
  expect(
    screen.getByRole("region", { name: "Delete account" }),
  ).toHaveAttribute("data-slot", "settings-section");
  expect(
    screen.getByRole("region", { name: "Delete account" }),
  ).toHaveAttribute("data-variant", "destructive");
});
