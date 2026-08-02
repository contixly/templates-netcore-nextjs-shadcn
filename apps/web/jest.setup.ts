import "@testing-library/jest-dom";

if (typeof Element !== "undefined") {
  Element.prototype.scrollIntoView = jest.fn();
}

if (typeof globalThis.ResizeObserver === "undefined") {
  globalThis.ResizeObserver = class ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
