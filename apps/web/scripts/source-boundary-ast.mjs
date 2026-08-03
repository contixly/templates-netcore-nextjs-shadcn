import typescript from "typescript";

const ts = typescript;
const sensitiveStorageTermPattern =
  /authorization|bearer|token|credential|password|secret|cookie|session/i;
const browserStorageNames = new Set(["localStorage", "sessionStorage"]);
const browserGlobalNames = new Set(["globalThis", "window"]);
const UNKNOWN_VALUE = Object.freeze({ kind: "unknown" });
const BROWSER_GLOBAL_VALUE = Object.freeze({ kind: "browser-global" });
const BROWSER_STORAGE_VALUE = Object.freeze({ kind: "browser-storage" });
const STORAGE_SET_ITEM_VALUE = Object.freeze({ kind: "storage-set-item" });
const STORAGE_SET_ITEM_BIND_VALUE = Object.freeze({
  kind: "storage-set-item-bind",
});
const STORAGE_SET_ITEM_CALL_VALUE = Object.freeze({
  kind: "storage-set-item-call",
});

function unwrapExpression(node) {
  let current = node;
  while (
    ts.isParenthesizedExpression(current) ||
    ts.isAsExpression(current) ||
    ts.isNonNullExpression(current) ||
    ts.isTypeAssertionExpression(current) ||
    ts.isSatisfiesExpression(current)
  ) {
    current = current.expression;
  }
  return current;
}

function propertyName(node) {
  const current = unwrapExpression(node);
  if (ts.isPropertyAccessExpression(current)) return current.name.text;
  if (!ts.isElementAccessExpression(current) || !current.argumentExpression) {
    return null;
  }
  const argument = unwrapExpression(current.argumentExpression);
  return ts.isStringLiteralLike(argument) ? argument.text : null;
}

class ValueScope {
  constructor(parent = null) {
    this.parent = parent;
    this.values = new Map();
  }

  child() {
    return new ValueScope(this);
  }

  declare(name, value = UNKNOWN_VALUE) {
    this.values.set(name, value);
  }

  assign(name, value) {
    if (this.values.has(name)) {
      this.values.set(name, value);
      return;
    }
    if (this.parent !== null && this.parent.has(name)) {
      this.parent.assign(name, value);
      return;
    }
    this.values.set(name, value);
  }

  has(name) {
    return this.values.has(name) || (this.parent?.has(name) ?? false);
  }

  lookup(name) {
    if (this.values.has(name)) return this.values.get(name);
    if (this.parent !== null && this.parent.has(name)) {
      return this.parent.lookup(name);
    }
    if (browserGlobalNames.has(name)) return BROWSER_GLOBAL_VALUE;
    if (browserStorageNames.has(name)) return BROWSER_STORAGE_VALUE;
    return UNKNOWN_VALUE;
  }
}

function stringValue(value) {
  return { kind: "string", value };
}

function staticPropertyName(node, scope) {
  const current = unwrapExpression(node);
  if (ts.isPropertyAccessExpression(current)) return current.name.text;
  if (!ts.isElementAccessExpression(current) || !current.argumentExpression) {
    return null;
  }
  const argument = unwrapExpression(current.argumentExpression);
  if (ts.isStringLiteralLike(argument)) return argument.text;
  if (ts.isIdentifier(argument)) {
    const value = scope.lookup(argument.text);
    return value.kind === "string" ? value.value : null;
  }
  return null;
}

function propertyValue(owner, name) {
  if (owner.kind === "browser-global") {
    if (browserGlobalNames.has(name) || name === "self") {
      return BROWSER_GLOBAL_VALUE;
    }
    if (browserStorageNames.has(name)) return BROWSER_STORAGE_VALUE;
  }
  if (owner.kind === "browser-storage" && name === "setItem") {
    return STORAGE_SET_ITEM_VALUE;
  }
  if (owner.kind === "storage-set-item" && name === "bind") {
    return STORAGE_SET_ITEM_BIND_VALUE;
  }
  if (owner.kind === "storage-set-item" && name === "call") {
    return STORAGE_SET_ITEM_CALL_VALUE;
  }
  return UNKNOWN_VALUE;
}

function bindingIdentifiers(name, identifiers = []) {
  if (ts.isIdentifier(name)) {
    identifiers.push(name.text);
  } else {
    for (const element of name.elements) {
      if (!ts.isOmittedExpression(element)) {
        bindingIdentifiers(element.name, identifiers);
      }
    }
  }
  return identifiers;
}

function predeclareStatements(statements, scope) {
  for (const statement of statements) {
    if (ts.isVariableStatement(statement)) {
      for (const declaration of statement.declarationList.declarations) {
        for (const name of bindingIdentifiers(declaration.name)) {
          scope.declare(name);
        }
      }
    } else if (
      (ts.isFunctionDeclaration(statement) ||
        ts.isClassDeclaration(statement) ||
        ts.isEnumDeclaration(statement)) &&
      statement.name
    ) {
      scope.declare(statement.name.text);
    } else if (ts.isImportDeclaration(statement) && statement.importClause) {
      const { importClause } = statement;
      if (importClause.name) scope.declare(importClause.name.text);
      const bindings = importClause.namedBindings;
      if (bindings && ts.isNamespaceImport(bindings)) {
        scope.declare(bindings.name.text);
      } else if (bindings) {
        for (const element of bindings.elements) {
          scope.declare(element.name.text);
        }
      }
    }
  }
}

function containsRawFetchCapability(sourceFile) {
  let found = false;

  const visit = (node) => {
    if (found) return;

    if (
      (ts.isPropertyAccessExpression(node) ||
        ts.isElementAccessExpression(node)) &&
      propertyName(node) === "fetch"
    ) {
      found = true;
      return;
    }

    if (ts.isIdentifier(node) && node.text === "fetch") {
      const parent = node.parent;
      const isNonValuePropertyName =
        (ts.isPropertyAccessExpression(parent) && parent.name === node) ||
        ((ts.isPropertyAssignment(parent) ||
          ts.isMethodDeclaration(parent) ||
          ts.isPropertyDeclaration(parent) ||
          ts.isPropertySignature(parent) ||
          ts.isMethodSignature(parent)) &&
          parent.name === node);

      if (!isNonValuePropertyName) {
        found = true;
        return;
      }
    }

    ts.forEachChild(node, visit);
  };

  visit(sourceFile);
  return found;
}

function containsSensitiveBrowserStorage(sourceFile) {
  let found = false;

  function isSensitive(node, value) {
    return (
      (value.kind === "string" &&
        sensitiveStorageTermPattern.test(value.value)) ||
      sensitiveStorageTermPattern.test(node.getText(sourceFile))
    );
  }

  function assignBinding(name, value, scope) {
    if (ts.isIdentifier(name)) {
      scope.assign(name.text, value);
      return;
    }
    if (ts.isObjectBindingPattern(name)) {
      for (const element of name.elements) {
        const nameNode = element.propertyName ?? element.name;
        const memberName = ts.isIdentifier(nameNode)
          ? nameNode.text
          : ts.isStringLiteralLike(nameNode)
            ? nameNode.text
            : null;
        const memberValue =
          memberName === null
            ? UNKNOWN_VALUE
            : propertyValue(value, memberName);
        assignBinding(element.name, memberValue, scope);
      }
      return;
    }
    for (const element of name.elements) {
      if (!ts.isOmittedExpression(element)) {
        assignBinding(element.name, UNKNOWN_VALUE, scope);
      }
    }
  }

  function analyzeFunction(node, parentScope) {
    const functionScope = parentScope.child();
    for (const parameter of node.parameters) {
      for (const name of bindingIdentifiers(parameter.name)) {
        functionScope.declare(name);
      }
      if (parameter.initializer) {
        const value = evaluate(parameter.initializer, functionScope);
        assignBinding(parameter.name, value, functionScope);
      }
    }
    if (node.body) visit(node.body, functionScope);
  }

  function evaluate(node, scope) {
    if (found) return UNKNOWN_VALUE;
    const current = unwrapExpression(node);

    if (ts.isIdentifier(current)) return scope.lookup(current.text);
    if (
      ts.isStringLiteralLike(current) ||
      ts.isNoSubstitutionTemplateLiteral(current)
    ) {
      return stringValue(current.text);
    }
    if (
      ts.isPropertyAccessExpression(current) ||
      ts.isElementAccessExpression(current)
    ) {
      const owner = evaluate(current.expression, scope);
      const name = staticPropertyName(current, scope);
      return name === null ? UNKNOWN_VALUE : propertyValue(owner, name);
    }
    if (ts.isCallExpression(current)) {
      const callee = evaluate(current.expression, scope);
      const argumentValues = current.arguments.map((argument) =>
        evaluate(argument, scope),
      );
      if (callee.kind === "storage-set-item") {
        if (
          current.arguments.some((argument, index) =>
            isSensitive(argument, argumentValues[index]),
          )
        ) {
          found = true;
        }
        return UNKNOWN_VALUE;
      }
      if (callee.kind === "storage-set-item-bind") {
        return STORAGE_SET_ITEM_VALUE;
      }
      if (callee.kind === "storage-set-item-call") {
        if (
          current.arguments
            .slice(1)
            .some((argument, index) =>
              isSensitive(argument, argumentValues[index + 1]),
            )
        ) {
          found = true;
        }
        return UNKNOWN_VALUE;
      }
      return UNKNOWN_VALUE;
    }
    if (ts.isBinaryExpression(current)) {
      if (current.operatorToken.kind === ts.SyntaxKind.EqualsToken) {
        const value = evaluate(current.right, scope);
        const left = unwrapExpression(current.left);
        if (ts.isIdentifier(left)) {
          scope.assign(left.text, value);
        } else if (
          ts.isPropertyAccessExpression(left) ||
          ts.isElementAccessExpression(left)
        ) {
          const owner = evaluate(left.expression, scope);
          if (
            owner.kind === "browser-storage" &&
            (isSensitive(
              left,
              stringValue(staticPropertyName(left, scope) ?? ""),
            ) ||
              isSensitive(current.right, value))
          ) {
            found = true;
          }
        }
        return value;
      }
      evaluate(current.left, scope);
      evaluate(current.right, scope);
      return UNKNOWN_VALUE;
    }
    if (
      ts.isArrowFunction(current) ||
      ts.isFunctionExpression(current) ||
      ts.isMethodDeclaration(current)
    ) {
      analyzeFunction(current, scope);
      return UNKNOWN_VALUE;
    }
    if (ts.isConditionalExpression(current)) {
      evaluate(current.condition, scope);
      evaluate(current.whenTrue, scope.child());
      evaluate(current.whenFalse, scope.child());
      return UNKNOWN_VALUE;
    }

    ts.forEachChild(current, (child) => visit(child, scope));
    return UNKNOWN_VALUE;
  }

  function visit(node, scope) {
    if (found) return;
    if (ts.isSourceFile(node)) {
      predeclareStatements(node.statements, scope);
      for (const statement of node.statements) visit(statement, scope);
      return;
    }
    if (ts.isBlock(node)) {
      const blockScope = scope.child();
      predeclareStatements(node.statements, blockScope);
      for (const statement of node.statements) visit(statement, blockScope);
      return;
    }
    if (ts.isVariableDeclaration(node)) {
      const value = node.initializer
        ? evaluate(node.initializer, scope)
        : UNKNOWN_VALUE;
      assignBinding(node.name, value, scope);
      return;
    }
    if (
      ts.isFunctionDeclaration(node) ||
      ts.isFunctionExpression(node) ||
      ts.isArrowFunction(node) ||
      ts.isMethodDeclaration(node) ||
      ts.isConstructorDeclaration(node) ||
      ts.isGetAccessorDeclaration(node) ||
      ts.isSetAccessorDeclaration(node)
    ) {
      analyzeFunction(node, scope);
      return;
    }
    if (
      ts.isCallExpression(node) ||
      ts.isBinaryExpression(node) ||
      ts.isPropertyAccessExpression(node) ||
      ts.isElementAccessExpression(node) ||
      ts.isConditionalExpression(node)
    ) {
      evaluate(node, scope);
      return;
    }
    ts.forEachChild(node, (child) => visit(child, scope));
  }

  visit(sourceFile, new ValueScope());
  return found;
}

export function findSourceBoundaryViolations(path, content) {
  const sourceFile = ts.createSourceFile(
    path,
    content,
    ts.ScriptTarget.Latest,
    true,
  );
  const violations = [];

  if (containsRawFetchCapability(sourceFile)) {
    violations.push("raw fetch outside generated runtime");
  }
  if (containsSensitiveBrowserStorage(sourceFile)) {
    violations.push("browser credential storage");
  }

  return violations;
}
