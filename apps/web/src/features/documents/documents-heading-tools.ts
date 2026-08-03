export function slugifyDocumentHeadingText(text: string): string {
  return text
    .trim()
    .toLowerCase()
    .replaceAll("ё", "е")
    .replace(/[^\p{Letter}\p{Number}\s-]/gu, "")
    .replace(/\s+/gu, "-")
    .replace(/-+/gu, "-")
    .replace(/^-|-$/gu, "");
}

export function createUniqueDocumentHeadingId(
  text: string,
  seen: Map<string, number>,
): string {
  const baseId = slugifyDocumentHeadingText(text) || "section";
  const count = (seen.get(baseId) ?? 0) + 1;
  seen.set(baseId, count);
  return count === 1 ? baseId : `${baseId}-${count}`;
}
