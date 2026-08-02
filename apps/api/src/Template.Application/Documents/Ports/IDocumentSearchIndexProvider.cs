namespace Template.Application.Documents.Ports;

public interface IDocumentSearchIndexProvider
{
    DocumentSearchLocaleIndex Get(DocumentLocale locale);
}
