# Application Header Separator Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Vertically center the 16 px separator between the application sidebar trigger and breadcrumbs without changing the shared separator primitive.

**Architecture:** Keep the fix at the `ApplicationHeader` call site by overriding the shared primitive's vertical stretch alignment with the matching `data-vertical:self-center` Tailwind variant. Lock the call-site contract with a focused React Testing Library test, then confirm actual computed geometry through the running Next.js application.

**Tech Stack:** Next.js 16.2.11 App Router, React, TypeScript, Tailwind CSS, Radix UI, Jest, React Testing Library, Codex in-app Browser.

## Global Constraints

- Modify only the application-header separator and its focused regression coverage.
- Do not change the shared `Separator` primitive.
- Do not change API, database, OpenAPI, or `template/` files.
- Keep the separator height, margins, width, color, and semantics unchanged.
- Work test-first: observe the focused test failing before implementation.
- Read the installed Next.js CSS documentation before changing Next.js code.
- Do not store browser screenshots or temporary QA artifacts in the repository.

---

## File Structure

- Modify `apps/web/test/components/application-header.test.tsx`: assert the application-header call site replaces vertical stretch with vertical center alignment.
- Modify `apps/web/src/features/application/ui/application-header.tsx`: apply the local alignment override.
- Read `apps/web/node_modules/next/dist/docs/01-app/01-getting-started/11-css.md`: satisfy the repository rule to consult installed Next.js documentation before editing web code.

### Task 1: Center the application-header separator

**Files:**
- Modify: `apps/web/test/components/application-header.test.tsx`
- Modify: `apps/web/src/features/application/ui/application-header.tsx:33`
- Read: `apps/web/node_modules/next/dist/docs/01-app/01-getting-started/11-css.md`

**Interfaces:**
- Consumes: the shared `Separator` component and its `data-slot="separator"` marker.
- Produces: an `ApplicationHeader` separator whose rendered classes include `data-vertical:self-center` and exclude `data-vertical:self-stretch`.

- [ ] **Step 1: Read the installed Next.js CSS guidance**

Run:

```bash
sed -n '1,240p' apps/web/node_modules/next/dist/docs/01-app/01-getting-started/11-css.md
```

Expected: the installed Next.js 16.2.11 documentation describes the supported global/Tailwind CSS integration; no dependency or configuration change is needed for a component utility-class edit.

- [ ] **Step 2: Write the failing component regression test**

Append this focused test to `apps/web/test/components/application-header.test.tsx`:

```tsx
it("centers the vertical separator in the header controls row", () => {
  render(
    <SidebarProvider defaultOpen={false}>
      <ApplicationHeader />
    </SidebarProvider>,
  );

  const separator = document.querySelector(
    '[data-slot="application-header"] [data-slot="separator"]',
  );

  expect(separator).toHaveClass("data-vertical:self-center");
  expect(separator).not.toHaveClass("data-vertical:self-stretch");
});
```

- [ ] **Step 3: Run the focused test and verify RED**

Run from `apps/web`:

```bash
npm test -- --runInBand test/components/application-header.test.tsx
```

Expected: FAIL because the rendered separator still contains
`data-vertical:self-stretch` and does not contain
`data-vertical:self-center`.

- [ ] **Step 4: Apply the minimal call-site override**

Change the separator in
`apps/web/src/features/application/ui/application-header.tsx` to:

```tsx
<Separator
  className="mx-1 h-4 data-vertical:self-center"
  orientation="vertical"
/>
```

Do not edit `apps/web/src/components/ui/separator.tsx`.

- [ ] **Step 5: Run the focused test and verify GREEN**

Run from `apps/web`:

```bash
npm test -- --runInBand test/components/application-header.test.tsx
```

Expected: all tests in `application-header.test.tsx` pass.

- [ ] **Step 6: Run relevant web verification**

Run from `apps/web`:

```bash
npm run typecheck
npm run lint
npm run format:check
npm test -- --runInBand
```

Expected: every command exits `0` with no test failures, TypeScript errors,
ESLint errors, or formatting differences.

- [ ] **Step 7: Verify rendered desktop behavior**

Using the running application at `https://localhost:3000/welcome`:

1. Reload after the source edit.
2. Confirm the page identity and meaningful DOM content.
3. Measure the application-header row and separator rectangles.
4. Assert that their vertical midpoints differ by no more than `1px` and the separator remains approximately `16px` high.
5. Toggle the desktop sidebar and repeat the midpoint assertion.
6. Confirm no relevant browser warning/error or framework overlay.
7. Capture an after screenshot outside the repository.

- [ ] **Step 8: Verify rendered mobile behavior**

Using a temporary mobile viewport such as `390x844`:

1. Reload `/welcome`.
2. Assert the header-row and separator midpoints differ by no more than `1px`.
3. Activate the sidebar trigger and verify the mobile sidebar opens.
4. Confirm the header has no clipping or overlap and no relevant browser errors.
5. Capture an after screenshot outside the repository.
6. Reset the viewport override before completing QA.

- [ ] **Step 9: Run repository guards**

Run from the repository root:

```bash
git diff --check
test -z "$(git diff --name-only -- template/)"
test -z "$(git status --porcelain -- template/)"
git status --short
```

Expected: clean diff formatting, no `template/` changes, and only the planned
test/source files remain uncommitted.

- [ ] **Step 10: Commit the implementation**

```bash
git add \
  apps/web/test/components/application-header.test.tsx \
  apps/web/src/features/application/ui/application-header.tsx
git commit -m "fix(web): center application header separator"
```

Expected: the implementation commit contains only the focused test and
application-header change.
