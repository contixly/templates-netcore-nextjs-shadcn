export const DOCUMENTS_SCROLL_CONTAINER_SELECTOR =
  "[data-documents-scroll-container]";
export const DEFAULT_DOCUMENTS_HEADING_ACTIVATION_OFFSET = 120;

export function readDocumentHash(hash: string): string | undefined {
  if (!hash.startsWith("#") || hash.length === 1) return undefined;

  try {
    return decodeURIComponent(hash.slice(1));
  } catch {
    return undefined;
  }
}

export function findActiveDocumentHeading(
  container: HTMLElement,
  headingIds: readonly string[],
): string | undefined {
  const threshold =
    container.getBoundingClientRect().top +
    DEFAULT_DOCUMENTS_HEADING_ACTIVATION_OFFSET;
  let active = headingIds[0];
  const headingsById = new Map(
    [...container.querySelectorAll<HTMLElement>("[id]")].map((heading) => [
      heading.id,
      heading,
    ]),
  );

  for (const id of headingIds) {
    const heading = headingsById.get(id);
    if (!heading) continue;
    if (heading.getBoundingClientRect().top > threshold) break;
    active = id;
  }

  return active;
}
