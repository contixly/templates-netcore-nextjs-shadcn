import { render, screen } from "@testing-library/react";
import { SettingsSection } from "@/src/features/application/ui/settings/settings-shell";

test("settings sections use the reference card/header/content composition", () => {
  render(
    <SettingsSection title="Profile" description="Manage profile">
      body
    </SettingsSection>,
  );
  expect(screen.getByRole("region", { name: "Profile" })).toHaveAttribute(
    "data-slot",
    "settings-section",
  );
  expect(document.querySelector("[data-slot='card-header']")).not.toBeNull();
  expect(document.querySelector("[data-slot='card-content']")).not.toBeNull();
});
