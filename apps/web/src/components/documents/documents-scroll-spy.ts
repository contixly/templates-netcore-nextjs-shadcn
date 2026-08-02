export const DOCUMENTS_SCROLL_CONTAINER_SELECTOR =
  "[data-documents-scroll-container]";

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
  const threshold = container.getBoundingClientRect().top + 96;
  let active = headingIds[0];

  for (const id of headingIds) {
    const heading = [...container.querySelectorAll<HTMLElement>("[id]")].find(
      (candidate) => candidate.id === id,
    );
    if (!heading) continue;
    if (heading.getBoundingClientRect().top > threshold) break;
    active = id;
  }

  return active;
}
