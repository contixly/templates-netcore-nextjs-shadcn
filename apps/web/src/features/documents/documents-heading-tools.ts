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

const reservedDocumentHeadingIds = [
  "document-title",
  "main-content",
  "footnote-label",
] as const;
const reservedGfmHeadingIdPrefixes = [
  "user-content-fn-",
  "user-content-fnref-",
] as const;

export function createDocumentHeadingIdState(): Map<string, number> {
  return new Map(reservedDocumentHeadingIds.map((id) => [id, 1]));
}

export function createUniqueDocumentHeadingId(
  text: string,
  seen: Map<string, number>,
): string {
  const slug = slugifyDocumentHeadingText(text) || "section";
  const baseId = reservedGfmHeadingIdPrefixes.some((prefix) =>
    slug.startsWith(prefix),
  )
    ? `document-heading-${slug}`
    : slug;
  let count = (seen.get(baseId) ?? 0) + 1;
  let id = count === 1 ? baseId : `${baseId}-${count}`;
  while (seen.has(id)) {
    count += 1;
    id = `${baseId}-${count}`;
  }

  seen.set(baseId, count);
  seen.set(id, 1);
  return id;
}
