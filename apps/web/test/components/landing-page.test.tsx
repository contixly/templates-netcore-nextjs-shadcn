import { render, screen } from "@testing-library/react";

import HomeLoading from "@/src/app/(public)/(home)/loading";
import english from "@/src/messages/application.en.json";
import russian from "@/src/messages/application.ru.json";

function messagePaths(value: unknown, prefix = ""): string[] {
  return Object.entries(value as Record<string, unknown>).flatMap(
    ([key, child]) => {
      const path = prefix ? `${prefix}.${key}` : key;
      return typeof child === "object" && child !== null
        ? messagePaths(child, path)
        : [path];
    },
  );
}

describe("landing page messages", () => {
  it("renders a deterministic no-data skeleton with one busy main landmark", () => {
    const { container } = render(<HomeLoading />);

    expect(screen.getByRole("banner")).toBeInTheDocument();
    expect(screen.getAllByRole("main")).toHaveLength(1);
    expect(screen.getByRole("main")).toHaveAttribute("aria-busy", "true");
    expect(container.querySelectorAll('[data-slot="skeleton"]')).toHaveLength(
      12,
    );
    expect(container).not.toHaveTextContent(/API status|Checking/);
  });

  it("keeps every English and Russian landing message at the same path", () => {
    expect(messagePaths(russian.landing).sort()).toEqual(
      messagePaths(english.landing).sort(),
    );
    expect(russian.landing.title).not.toBe(english.landing.title);
  });

  it("describes the target architecture without reference-stack product claims", () => {
    const englishCopy = JSON.stringify(english.landing);
    const russianCopy = JSON.stringify(russian.landing);

    expect(englishCopy).toMatch(/ASP\.NET Core 10/);
    expect(englishCopy).toMatch(/REST/);
    expect(russianCopy).toMatch(/ASP\.NET Core 10/);
    expect(russianCopy).toMatch(/REST/);
    expect(`${englishCopy} ${russianCopy}`).not.toMatch(
      /Better Auth|Prisma|Server Actions/,
    );
  });

  it("localizes generic technical nouns in the Russian landing copy", () => {
    const russianCopy = JSON.stringify(russian.landing);

    expect(russianCopy).toMatch(/REST/);
    expect(russianCopy).toMatch(/HttpOnly/);
    expect(russianCopy).not.toMatch(/\b(?:endpoints|cookies|origin)\b/i);
  });
});
