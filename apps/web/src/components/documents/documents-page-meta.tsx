import { useTranslations } from "next-intl";

import { Badge } from "@/src/components/ui/badge";
import type { DocumentInfo } from "@/src/features/documents/documents-types";

export function DocumentsPageMeta({
  document,
}: Readonly<{ document: DocumentInfo }>) {
  const t = useTranslations("documents.meta");
  const metadata = [
    [t("group"), document.meta.group],
    [t("parent"), document.meta.parentItem],
    [t("purpose"), document.meta.purpose],
    [t("author"), document.meta.author],
    [t("version"), document.meta.version],
    [t("reading"), document.meta.reading],
  ].filter((item): item is [string, string] => Boolean(item[1]));

  return (
    <div className="flex flex-col gap-4 border-y py-4 text-xs">
      <dl className="grid gap-3 sm:grid-cols-2">
        {metadata.map(([label, value]) => (
          <div className="flex flex-col gap-1" key={label}>
            <dt className="text-muted-foreground">{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
        {document.meta.editedAt ? (
          <div className="flex flex-col gap-1">
            <dt className="text-muted-foreground">{t("editedAt")}</dt>
            <dd>
              <time dateTime={document.meta.editedAt}>
                {document.meta.editedAt}
              </time>
            </dd>
          </div>
        ) : null}
      </dl>
      <div className="flex flex-wrap gap-2">
        <Badge variant="secondary">{t(`status.${document.meta.status}`)}</Badge>
        <Badge variant="outline">
          {document.meta.hide
            ? t("visibility.hidden")
            : t("visibility.visible")}
        </Badge>
        <Badge variant="outline">
          {t(`language.${document.contentLocale}`)}
        </Badge>
        {document.isLocaleFallback ? (
          <Badge variant="outline">
            {t(`fallback.${document.contentLocale}`)}
          </Badge>
        ) : null}
      </div>
    </div>
  );
}
