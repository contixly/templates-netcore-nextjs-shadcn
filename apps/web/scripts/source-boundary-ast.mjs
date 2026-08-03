import typescript from "typescript";

const ts = typescript;
const reservedCapabilityViolations = new Map([
  ["fetch", "raw fetch outside generated runtime"],
  ["localStorage", "browser credential storage"],
  ["sessionStorage", "browser credential storage"],
]);
const rawProductApiPathPattern = /\/api\/v1(?:[/?#]|$)/u;

function staticTokenText(node) {
  if (
    ts.isIdentifier(node) ||
    ts.isStringLiteralLike(node) ||
    ts.isTemplateHead(node) ||
    ts.isTemplateMiddle(node) ||
    ts.isTemplateTail(node)
  ) {
    return node.text;
  }

  return null;
}

export function findSourceBoundaryViolations(path, content) {
  const sourceFile = ts.createSourceFile(
    path,
    content,
    ts.ScriptTarget.Latest,
    true,
  );
  const violations = new Set();

  function visit(node) {
    const text = staticTokenText(node);

    if (text !== null) {
      const capabilityViolation = reservedCapabilityViolations.get(text);
      if (capabilityViolation) {
        violations.add(capabilityViolation);
      }

      if (!ts.isIdentifier(node) && rawProductApiPathPattern.test(text)) {
        violations.add("raw product API path");
      }
    }

    ts.forEachChild(node, visit);
  }

  visit(sourceFile);
  return [...violations];
}
