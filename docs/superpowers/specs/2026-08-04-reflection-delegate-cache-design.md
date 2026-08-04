# Reflection Delegate Cache Design

## Goal

Reduce repeated reflection invocation overhead in two bounded areas without
changing gameplay behavior or making mod startup dependent on runtime delegate
compilation:

- special-army and royal-guard `ArmyManager.newObject` calls;
- hierarchical vassal map `selectAndInspect` calls.

Low-frequency reflection used by occupation settlement, city transfer,
nameplate setup, and world-age UI remains unchanged.

## Approach

Add an internal `ReflectionDelegateFactory` that converts a `MethodInfo` into a
requested strongly typed delegate with expression trees. The factory uses the
method's `DeclaringType` for instance conversion, validates instance/argument
counts, converts compatible delegate arguments and return values explicitly,
and reports failure without throwing during static initialization.

Call sites cache the compiled delegate once. They call the delegate directly,
never through `DynamicInvoke`, so successful compilation avoids reflection
argument arrays, boxing introduced by `MethodInfo.Invoke`, and wrapped
`TargetInvocationException` results.

## Army Creation

`AWArmyService` and `RoyalGuardService` retain their existing method discovery.
Each service initializes a cached strongly typed army factory immediately after
its cached `MethodInfo`.

Creation uses the delegate when available and otherwise invokes the cached
`MethodInfo`. The existing null checks, cleanup queue, exception handling, role
tracking, and army initialization order remain unchanged. A delegate compile
failure therefore affects performance only, not army availability.

## Map Selection

`HierarchicalVassalMapModeService` continues resolving the current meta asset
from `MetaTypeLibrary`. It caches an invocation adapter by concrete asset type
and `selectAndInspect` method signature. Supported one-, three-, and
four-parameter signatures preserve the current default `false` arguments.

If a signature cannot be compiled, the adapter uses the existing reflection
call. Missing assets or methods still return `false`, and invocation exceptions
remain contained by the current caller.

The cache is bounded by the small set of meta asset runtime types and does not
retain world objects.

## Compatibility And Failure Handling

- Target framework remains `net48`; no new dependency is introduced.
- Private inherited methods use `DeclaringType`, not `ReflectedType`.
- Static initialization must not fail when expression compilation is unavailable
  or a game update changes a signature.
- Delegate and reflection paths must return the same object/result for supported
  signatures.
- No RTS state machine, recruitment, withdrawal, or tactical handoff behavior is
  changed.

## Tests

Add focused tests for the shared factory before production integration:

- an instance method is compiled and receives the correct instance and values;
- a static method and return value are supported;
- object-typed delegate parameters are converted to concrete method parameters;
- incompatible signatures fail cleanly so callers can use reflection fallback.

Then run the complete rules suite, RTS adversarial simulation, and the net48 mod
build. Production integration is additionally checked for absence of
`DynamicInvoke` and retention of explicit reflection fallbacks.
