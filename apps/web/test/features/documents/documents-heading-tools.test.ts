import {
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
