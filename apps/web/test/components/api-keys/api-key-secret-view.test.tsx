import { act, fireEvent, screen } from "@testing-library/react";
import { useRef } from "react";

import {
  ApiKeySecretView,
  type ApiKeySecretViewHandle,
} from "@/src/features/api-keys/ui/api-key-secret-view";
import { deferred } from "@/test/components/api-keys/fixtures";
import { renderWithMessages } from "@/test/support/render";

function SecretHarness() {
  const secret = useRef<ApiKeySecretViewHandle>(null);
  return (
    <>
      <button onClick={() => secret.current?.reveal("credential-a")}>
        Reveal A
      </button>
      <button onClick={() => secret.current?.reveal("credential-b")}>
        Reveal B
      </button>
      <ApiKeySecretView ref={secret} />
    </>
  );
}

it("binds clipboard completion to the revealed credential generation", async () => {
  const firstCopy = deferred<void>();
  const writeText = jest
    .fn<Promise<void>, [string]>()
    .mockReturnValueOnce(firstCopy.promise)
    .mockRejectedValueOnce(new Error("clipboard denied"));
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText },
  });
  renderWithMessages(<SecretHarness />);

  fireEvent.click(screen.getByRole("button", { name: "Reveal A" }));
  fireEvent.click(screen.getByRole("button", { name: "Copy credential" }));
  fireEvent.click(screen.getByRole("button", { name: "I saved it" }));
  fireEvent.click(screen.getByRole("button", { name: "Reveal B" }));
  await act(async () => firstCopy.resolve());

  expect(screen.getByText("credential-b")).toBeVisible();
  expect(screen.queryByText("Credential copied")).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: "Copy credential" }));
  expect(
    await screen.findByText(
      "The credential could not be copied. Select and copy it manually.",
    ),
  ).toBeVisible();
  expect(writeText).toHaveBeenNthCalledWith(1, "credential-a");
  expect(writeText).toHaveBeenNthCalledWith(2, "credential-b");
});

it("ignores clipboard completion after unmount", async () => {
  const copy = deferred<void>();
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText: jest.fn().mockReturnValue(copy.promise) },
  });
  const view = renderWithMessages(<SecretHarness />);

  fireEvent.click(screen.getByRole("button", { name: "Reveal A" }));
  fireEvent.click(screen.getByRole("button", { name: "Copy credential" }));
  view.unmount();
  await act(async () => copy.resolve());
});
