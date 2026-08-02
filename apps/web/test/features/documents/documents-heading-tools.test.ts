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
