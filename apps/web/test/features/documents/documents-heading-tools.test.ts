import {
  createDocumentHeadingIdState,
  createUniqueDocumentHeadingId,
  slugifyDocumentHeadingText,
} from "@/src/features/documents/documents-heading-tools";

it("normalizes generated heading IDs and gives duplicates stable suffixes", () => {
  const seen = new Map<string, number>();

  expect(slugifyDocumentHeadingText("  Ёж: API & UI  ")).toBe("еж-api-ui");
  expect(createUniqueDocumentHeadingId("Раздел", seen)).toBe("раздел");
  expect(createUniqueDocumentHeadingId("Раздел", seen)).toBe("раздел-2");
  expect(createUniqueDocumentHeadingId("!!!", seen)).toBe("section");
});

it("starts document heading allocation after reserved runtime-owned IDs", () => {
  const seen = createDocumentHeadingIdState();

  expect(createUniqueDocumentHeadingId("Document title", seen)).toBe(
    "document-title-2",
  );
  expect(createUniqueDocumentHeadingId("Document title 2", seen)).toBe(
    "document-title-2-2",
  );
  expect(createUniqueDocumentHeadingId("Main content", seen)).toBe(
    "main-content-2",
  );
  expect(createUniqueDocumentHeadingId("Footnote label", seen)).toBe(
    "footnote-label-2",
  );

  const reverseOrder = createDocumentHeadingIdState();
  expect(createUniqueDocumentHeadingId("Document title 2", reverseOrder)).toBe(
    "document-title-2",
  );
  expect(createUniqueDocumentHeadingId("Document title", reverseOrder)).toBe(
    "document-title-3",
  );
});

it("never reuses a suffix claimed by a different heading base", () => {
  const seen = new Map<string, number>();

  expect(createUniqueDocumentHeadingId("!!!", seen)).toBe("section");
  expect(createUniqueDocumentHeadingId("!!!", seen)).toBe("section-2");
  expect(createUniqueDocumentHeadingId("Section 2", seen)).toBe("section-2-2");
});

it("keeps article headings outside GFM's dynamic footnote ID namespaces", () => {
  const seen = createDocumentHeadingIdState();

  expect(createUniqueDocumentHeadingId("User content fn note", seen)).toBe(
    "document-heading-user-content-fn-note",
  );
  expect(createUniqueDocumentHeadingId("User content fnref note", seen)).toBe(
    "document-heading-user-content-fnref-note",
  );
});

it("uses locale-invariant casing when the host default locale is Turkish", () => {
  const originalToLocaleLowerCase = String.prototype.toLocaleLowerCase;
  const localeSpy = jest
    .spyOn(String.prototype, "toLocaleLowerCase")
    .mockImplementation(function (this: string, locales) {
      return originalToLocaleLowerCase.call(this, locales ?? "tr");
    });

  try {
    expect(slugifyDocumentHeadingText("Iİ API")).toBe("ii-api");
  } finally {
    localeSpy.mockRestore();
  }
});
