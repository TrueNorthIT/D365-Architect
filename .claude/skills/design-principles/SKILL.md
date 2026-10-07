---
name: design-principles
description: General software engineering practices — clear naming, small focused units, explicit boundaries, useful errors, tests, documenting why, consistency, minimal scope, and safe handling of destructive actions. Use when designing, writing, or reviewing any code, regardless of language or project.
---

# Design principles

General practices that apply to any codebase. Project-specific skills add the local conventions on top; where they conflict, the project's skill wins.

## Structure

- **One reason to change.** Give each function, class and module a single, nameable responsibility. If describing it needs "and", consider splitting it.
- **Separate what varies from what doesn't.** When behaviour differs per case, express the cases as data or small implementations and keep one generic mechanism that uses them, rather than growing branches.
- **Depend on narrow contracts.** Pass in what a unit needs (an interface, a function) rather than reaching for globals or concrete types. It keeps units testable and replaceable.
- **Don't abstract early.** Wait for the second or third real use before generalising. Duplication is cheaper than the wrong abstraction, but once a pattern repeats, extract it and document it.
- **Keep layers thin.** Entry points (CLI, HTTP, UI) parse input and call into logic; they don't contain it.

## Clarity

- **Names say what and why, not how.** Prefer a longer accurate name over a short vague one. A reader should not need the body to understand the call site.
- **Make the common path obvious.** Early returns and guard clauses over deep nesting. Keep the happy path unindented.
- **Prefer explicit over implicit.** No hidden side effects, no behaviour that depends on call order or ambient state unless it is documented and unavoidable.
- **Model the domain with types.** Use enums, value types and non-nullable references to make invalid states hard to express, instead of strings and flags checked by convention.

## Correctness and errors

- **Validate at boundaries, trust inside.** Check external input once, where it enters; internal code can then rely on it.
- **Fail early and clearly.** An error should say what went wrong, with which input, and what to do next. Don't swallow exceptions, and don't return a plausible-looking wrong answer.
- **No silent fixes.** If you coerce, default or skip something, make it visible (log, message, return value), not hidden.
- **Handle the edges.** Empty, null, duplicate, very large and concurrent cases deserve a decision, even if the decision is "reject".

## Testing

- **Write tests first when possible.** Write the test for new behaviour before the code that provides it: watch it fail for the right reason, then make it pass. It pins down the intended behaviour up front and keeps the design testable. When a test-first approach is impractical (exploratory work, hard-to-isolate code), add the tests in the same change, not later.
- **Run the tests after every batch of changes**, before handing work back. A quick run catches regressions while the cause is still fresh; report the result honestly, including failures.
- **Test behaviour, not implementation.** Assert on observable results so refactors don't break tests that should still pass.
- **Reproduce bugs first.** Write a test that fails for the reported reason, then make it pass. Confirm it fails without the fix.
- **Use realistic inputs.** Prefer real samples over invented ones; they carry the details that break things.
- **Keep tests small and independent.** One idea per test, no ordering dependencies, descriptive names.
- **Change tests when the design changes.** Update or replace tests that encode an old decision rather than weakening or deleting them without thought.

## Documentation

- **Comment the why.** The code shows what it does; comments record the reason, the evidence, the alternatives rejected and the known limits.
- **Keep docs next to the thing they describe** and update them in the same change.
- **State assumptions and trade-offs** where the decision lives, so the next reader can judge them.

## Change management

- **Keep changes small and focused.** One purpose per change; no unrelated refactors or formatting churn mixed in.
- **Match the surrounding code.** Follow existing style, naming, structure and comment density. Introduce a new pattern only when it will be reused, and then document it.
- **Leave it better, not different.** Fix what you touch; note, rather than silently fix, what you don't.
- **Prefer reversible steps.** Small commits, feature flags and additive changes make mistakes cheap to undo.

## Safety

- **Destructive or outward-facing actions need confirmation.** Show what will change first, and require an explicit go-ahead. Never delete or overwrite implicitly.
- **Least privilege and least surprise.** Do only what was asked, with the minimum access needed, and say plainly when something has a side effect beyond the stated goal.
- **Never commit secrets.** Keep credentials and tokens out of code, logs and test data.
- **Report honestly.** Say what was verified, what wasn't, and what failed, with the evidence.
