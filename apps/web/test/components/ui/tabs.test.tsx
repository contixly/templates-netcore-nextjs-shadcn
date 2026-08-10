import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";

import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/src/components/ui/tabs";

test("forwards vertical orientation to Radix semantics and keyboard navigation", async () => {
  render(
    <Tabs defaultValue="profile" orientation="vertical">
      <TabsList aria-label="Settings">
        <TabsTrigger value="profile">Profile</TabsTrigger>
        <TabsTrigger value="security">Security</TabsTrigger>
      </TabsList>
      <TabsContent value="profile">Profile settings</TabsContent>
      <TabsContent value="security">Security settings</TabsContent>
    </Tabs>,
  );

  expect(screen.getByRole("tablist", { name: "Settings" })).toHaveAttribute(
    "aria-orientation",
    "vertical",
  );

  const profile = screen.getByRole("tab", { name: "Profile" });
  const security = screen.getByRole("tab", { name: "Security" });
  act(() => profile.focus());
  fireEvent.keyDown(profile, { key: "ArrowDown" });
  await waitFor(() => expect(security).toHaveFocus());
});
