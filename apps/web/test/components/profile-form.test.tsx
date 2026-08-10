import { fireEvent, screen, waitFor } from "@testing-library/react";

import { ProfileForm } from "@/src/features/account/ui/profile-form";
import { updateBrowserAccountProfile } from "@/src/lib/api/account/browser/account-mutations";
import type { AccountResponse } from "@/src/lib/api/generated";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: () => ({ id: "browser-client" }),
}));
jest.mock("@/src/lib/api/account/browser/account-mutations", () => ({
  updateBrowserAccountProfile: jest.fn(),
}));

const account = {
  id: "01900000-0000-7000-8000-000000000001",
  displayName: "Account User",
  primaryEmail: "account@example.test",
  imageUrl: "https://images.example.test/avatar.png",
  createdAt: "2026-07-28T09:30:00Z",
  verifiedEmails: [
    {
      email: "account@example.test",
      isPrimary: true,
      providers: ["google"],
    },
    {
      email: "secondary@example.test",
      isPrimary: false,
      providers: ["github"],
    },
  ],
} satisfies AccountResponse;

const updateProfile = jest.mocked(updateBrowserAccountProfile);

beforeEach(() => {
  jest.clearAllMocks();
});

it("subordinates its internal sections beneath a settings section heading", () => {
  renderWithMessages(<ProfileForm headingLevel={3} initialAccount={account} />);

  for (const name of [
    "Profile avatar",
    "Display name",
    "Verified email addresses",
  ]) {
    expect(screen.getByRole("heading", { level: 3, name })).toBeVisible();
  }
  expect(screen.queryByRole("heading", { level: 2 })).not.toBeInTheDocument();
});

it("shows immutable account identifiers, verified emails, and creation date", () => {
  renderWithMessages(<ProfileForm initialAccount={account} />);

  expect(screen.getByRole("textbox", { name: "Display name" })).toHaveValue(
    "Account User",
  );
  expect(screen.getByText("account@example.test")).toBeInTheDocument();
  expect(screen.getByText("secondary@example.test")).toBeInTheDocument();
  expect(screen.getByText(account.id)).toBeInTheDocument();
  expect(screen.getByText("Jul 28, 2026")).toBeInTheDocument();
  expect(screen.getAllByRole("textbox")).toHaveLength(1);
});

it("renders the canonical primary email independently of secondary emails", () => {
  renderWithMessages(
    <ProfileForm
      initialAccount={{
        ...account,
        verifiedEmails: account.verifiedEmails.filter(
          (email) => !email.isPrimary,
        ),
      }}
    />,
  );

  expect(screen.getByText("account@example.test")).toBeInTheDocument();
  expect(screen.getByText("secondary@example.test")).toBeInTheDocument();
});

it.each([
  [" ", "Use at least 2 characters."],
  [" a ", "Use at least 2 characters."],
  ["x".repeat(51), "Use 50 characters or fewer."],
  ["😀".repeat(26), "Use 50 characters or fewer."],
])("rejects an invalid trimmed display name %p", async (value, message) => {
  renderWithMessages(<ProfileForm initialAccount={account} />);

  fireEvent.change(screen.getByRole("textbox", { name: "Display name" }), {
    target: { value },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save profile" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(message);
  expect(updateProfile).not.toHaveBeenCalled();
});

it.each(["😀", "😀".repeat(25)])(
  "accepts %p using the API's UTF-16 display-name length",
  async (value) => {
    updateProfile.mockResolvedValue({
      ok: true,
      data: { ...account, displayName: value },
    });
    renderWithMessages(<ProfileForm initialAccount={account} />);

    fireEvent.change(screen.getByRole("textbox", { name: "Display name" }), {
      target: { value },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save profile" }));

    await waitFor(() => {
      expect(updateProfile).toHaveBeenCalledWith(
        { id: "browser-client" },
        { displayName: value },
      );
    });
  },
);

it("submits the trimmed name and renders only the confirmed projection", async () => {
  updateProfile.mockResolvedValue({
    ok: true,
    data: { ...account, displayName: "Updated User" },
  });
  renderWithMessages(<ProfileForm initialAccount={account} />);

  fireEvent.change(screen.getByRole("textbox", { name: "Display name" }), {
    target: { value: "  Updated User  " },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save profile" }));

  await waitFor(() => {
    expect(updateProfile).toHaveBeenCalledWith(
      { id: "browser-client" },
      { displayName: "Updated User" },
    );
  });
  expect(await screen.findByRole("status")).toHaveTextContent(
    "Profile updated.",
  );
  expect(screen.getByRole("textbox", { name: "Display name" })).toHaveValue(
    "Updated User",
  );
});

it("renders a safe recoverable mutation failure", async () => {
  updateProfile
    .mockResolvedValueOnce({
      ok: false,
      failure: {
        kind: "problem",
        code: "validation_failed",
        status: 400,
        traceId: "trace-profile",
      },
    })
    .mockResolvedValueOnce({
      ok: true,
      data: { ...account, displayName: "Recovered User" },
    });
  renderWithMessages(<ProfileForm initialAccount={account} />);

  const name = screen.getByRole("textbox", { name: "Display name" });
  const submit = screen.getByRole("button", { name: "Save profile" });
  fireEvent.change(name, { target: { value: "Recovered User" } });
  fireEvent.click(submit);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Profile could not be updated.",
  );
  expect(screen.getByRole("alert")).toHaveTextContent("trace-profile");
  expect(submit).toBeEnabled();

  fireEvent.click(submit);
  expect(await screen.findByRole("status")).toHaveTextContent(
    "Profile updated.",
  );
});
