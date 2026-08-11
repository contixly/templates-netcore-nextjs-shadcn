# Application Header Separator Alignment Design

## Context

The vertical separator between the application sidebar trigger and breadcrumbs
is rendered at the top of the header content row instead of being vertically
centered. Browser measurements on `/welcome` show a 32 px content row starting
at approximately `y=7.7`, while the 16 px separator starts at the same `y`.
Its expected centered position is approximately `y=15.7`.

The shared `Separator` primitive applies
`data-vertical:self-stretch`. `ApplicationHeader` supplies `h-4`, which limits
the separator height but does not center the resulting flex item.

## Decision

Override alignment only at the application-header call site by adding
`data-vertical:self-center` to the separator classes. The variant-matched class
replaces the primitive's vertical `self-stretch` behavior while retaining the
existing 16 px height, horizontal margins, color, width, and semantics.

This is preferred over adding a wrapper because it avoids unnecessary markup,
and over changing the shared primitive because other vertical separators may
depend on its stretch default.

## Scope

In scope:

- the separator in `ApplicationHeader`;
- a focused component regression test for the call-site alignment override;
- rendered desktop and mobile verification of the header;
- checking the sidebar trigger interaction and browser console after the fix.

Out of scope:

- changes to the shared `Separator` primitive;
- other application-shell layout changes;
- API, database, OpenAPI, or reference-template changes;
- unrelated visual refactoring.

## Test Strategy

1. Add a failing component test proving that the application-header separator
   uses the vertical `self-center` override and no longer exposes the vertical
   `self-stretch` class.
2. Apply the one-class call-site fix and rerun the focused test.
3. Run the relevant web test, typecheck, lint, and formatting checks.
4. Reload `/welcome` and verify that the separator midpoint matches the header
   row midpoint on desktop and a mobile viewport.
5. Toggle the sidebar and confirm the header remains aligned with no relevant
   browser warnings or errors.

## Acceptance Criteria

- The 16 px separator is vertically centered in the application-header row.
- Sidebar expanded and collapsed states keep the same centered alignment.
- The fix is local to `ApplicationHeader` and does not alter the shared
  primitive.
- Focused tests and relevant web checks pass.
- `template/` remains unchanged.
