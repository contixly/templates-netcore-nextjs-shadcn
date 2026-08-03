import type {
  DocumentInfo,
  DocumentNavigationItem,
  DocumentPageNavigation,
  DocumentsSidebarGroup,
  DocumentsSidebarParent,
} from "./documents-types";

function toNavigationItem(document: DocumentInfo): DocumentNavigationItem {
  return {
    canonicalUrl: document.canonicalUrl,
    href: document.href,
    title: document.meta.title,
    description: document.meta.description,
  };
}

export function buildDocumentsSidebarNavigation(
  documents: DocumentInfo[],
): DocumentsSidebarGroup[] {
  const groups = new Map<string, Map<string, DocumentsSidebarParent>>();

  for (const document of documents) {
    let parents = groups.get(document.meta.group);
    if (!parents) {
      parents = new Map();
      groups.set(document.meta.group, parents);
    }

    let parent = parents.get(document.meta.parentItem);
    if (!parent) {
      parent = { label: document.meta.parentItem, items: [] };
      parents.set(document.meta.parentItem, parent);
    }

    parent.items.push({
      canonicalUrl: document.canonicalUrl,
      label: document.meta.title,
      href: document.href,
      status: document.meta.status,
    });
  }

  return [...groups].map(([label, parents]) => ({
    label,
    parents: [...parents.values()],
  }));
}

export function buildDocumentPageNavigation(
  documents: DocumentInfo[],
  canonicalUrl: string,
): DocumentPageNavigation {
  const index = documents.findIndex(
    (document) => document.canonicalUrl === canonicalUrl,
  );

  if (index < 0) return {};

  const previous = documents[index - 1];
  const next = documents[index + 1];

  return {
    ...(previous ? { previous: toNavigationItem(previous) } : {}),
    ...(next ? { next: toNavigationItem(next) } : {}),
  };
}
