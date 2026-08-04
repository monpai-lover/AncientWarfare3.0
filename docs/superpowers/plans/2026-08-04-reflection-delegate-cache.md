# Reflection Delegate Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cache strongly typed delegates for army creation and hierarchical-map selection while retaining reflection fallbacks on incompatible runtimes.

**Architecture:** A dependency-free `ReflectionDelegateFactory` converts a `MethodInfo` to a requested delegate by building explicit instance, argument, and return conversions. Army services cache one delegate each; the hierarchical map service caches a small adapter per concrete meta-asset type, and every call site retains its current `MethodInfo.Invoke` behavior when compilation is unavailable.

**Tech Stack:** C# 11, .NET Framework 4.8, `System.Linq.Expressions`, reflection, .NET 9 console rule tests.

---

### Task 1: Tested reflection delegate factory

**Files:**
- Create: `Code/utils/ReflectionDelegateFactory.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ReflectionDelegateFactoryTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add the failing factory tests**

Create `ReflectionDelegateFactoryTests.cs.txt` with a private fixture and four focused assertions:

```csharp
using System.Reflection;
using AncientWarfare3.utils;

internal static class ReflectionDelegateFactoryTests
{
    private delegate int ObjectAdd(object instance, object left, object right);
    private delegate void ObjectSet(object instance, object value);

    public static void Run()
    {
        InstanceArgumentsAreConverted();
        StaticReturnValueIsPreserved();
        VoidMethodIsSupported();
        UnusedReturnValueCanBeDiscarded();
        IncompatibleSignatureReturnsNull();
    }

    private static void InstanceArgumentsAreConverted()
    {
        MethodInfo method = typeof(Fixture).GetMethod("Add",
            BindingFlags.Instance | BindingFlags.NonPublic);
        ObjectAdd call = ReflectionDelegateFactory.TryCreate<ObjectAdd>(method);
        Equal(7, call(new Fixture(), 3, 4), "instance delegate conversion");
    }

    private static void StaticReturnValueIsPreserved()
    {
        MethodInfo method = typeof(Fixture).GetMethod("Double",
            BindingFlags.Static | BindingFlags.NonPublic);
        Func<int, int> call =
            ReflectionDelegateFactory.TryCreate<Func<int, int>>(method);
        Equal(12, call(6), "static delegate return");
    }

    private static void VoidMethodIsSupported()
    {
        MethodInfo method = typeof(Fixture).GetMethod("Set",
            BindingFlags.Instance | BindingFlags.NonPublic);
        ObjectSet call = ReflectionDelegateFactory.TryCreate<ObjectSet>(method);
        var fixture = new Fixture();
        call(fixture, 9);
        Equal(9, fixture.Value, "void delegate invocation");
    }

    private static void UnusedReturnValueCanBeDiscarded()
    {
        MethodInfo method = typeof(Fixture).GetMethod("Double",
            BindingFlags.Static | BindingFlags.NonPublic);
        Action<int> call =
            ReflectionDelegateFactory.TryCreate<Action<int>>(method);
        call(5);
    }

    private static void IncompatibleSignatureReturnsNull()
    {
        MethodInfo method = typeof(Fixture).GetMethod("Double",
            BindingFlags.Static | BindingFlags.NonPublic);
        Func<string, string> call =
            ReflectionDelegateFactory.TryCreate<Func<string, string>>(method);
        Equal<Func<string, string>>(null, call,
            "incompatible delegate fallback");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " +
                expected + ", got " + actual);
    }

    private sealed class Fixture
    {
        internal int Value { get; private set; }
        private int Add(int left, int right) => left + right;
        private static int Double(int value) => value * 2;
        private void Set(int value) => Value = value;
    }
}
```

Add the test and future production file to the test project:

```xml
<Compile Include="ReflectionDelegateFactoryTests.cs.txt" />
<Compile Include="..\..\Code\utils\ReflectionDelegateFactory.cs"
         Link="Production\ReflectionDelegateFactory.cs" />
```

Add `ReflectionDelegateFactoryTests.Run();` to the full test sequence in
`Program.cs.txt` immediately before the final `Rule tests passed.` output.

- [ ] **Step 2: Run the rules project and verify the red failure**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
```

Expected: build failure because `AncientWarfare3.utils.ReflectionDelegateFactory` does not exist.

- [ ] **Step 3: Implement the minimal factory**

Create `Code/utils/ReflectionDelegateFactory.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace AncientWarfare3.utils
{
    internal static class ReflectionDelegateFactory
    {
        internal static TDelegate TryCreate<TDelegate>(MethodInfo pMethod)
            where TDelegate : Delegate
        {
            if (pMethod == null) return null;
            try
            {
                MethodInfo invoke = typeof(TDelegate).GetMethod("Invoke");
                ParameterInfo[] delegateParameters = invoke.GetParameters();
                ParameterInfo[] methodParameters = pMethod.GetParameters();
                int instanceOffset = pMethod.IsStatic ? 0 : 1;
                if (delegateParameters.Length !=
                    methodParameters.Length + instanceOffset) return null;

                var lambdaParameters = new List<ParameterExpression>();
                foreach (ParameterInfo parameter in delegateParameters)
                    lambdaParameters.Add(Expression.Parameter(
                        parameter.ParameterType, parameter.Name));

                Expression instance = null;
                if (!pMethod.IsStatic)
                {
                    Type declaringType = pMethod.DeclaringType;
                    if (declaringType == null) return null;
                    instance = Convert(lambdaParameters[0], declaringType);
                }

                var arguments = new Expression[methodParameters.Length];
                for (int i = 0; i < arguments.Length; i++)
                    arguments[i] = Convert(lambdaParameters[i + instanceOffset],
                        methodParameters[i].ParameterType);

                MethodCallExpression call = pMethod.IsStatic
                    ? Expression.Call(pMethod, arguments)
                    : Expression.Call(instance, pMethod, arguments);
                Expression body;
                if (invoke.ReturnType == typeof(void))
                    body = pMethod.ReturnType == typeof(void)
                        ? (Expression)call
                        : Expression.Block(call, Expression.Empty());
                else
                    body = Convert(call, invoke.ReturnType);
                return Expression.Lambda<TDelegate>(body,
                    lambdaParameters).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Expression Convert(Expression pValue, Type pTarget)
        {
            return pValue.Type == pTarget
                ? pValue
                : Expression.Convert(pValue, pTarget);
        }
    }
}
```

- [ ] **Step 4: Run the rules suite and verify green**

Run the same `dotnet run` command.

Expected: `Rule tests passed.`

- [ ] **Step 5: Commit the tested factory**

```powershell
git add Code\utils\ReflectionDelegateFactory.cs `
  Tests\AncientWarfare3.Rules.Tests\ReflectionDelegateFactoryTests.cs.txt `
  Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj `
  Tests\AncientWarfare3.Rules.Tests\Program.cs.txt
git commit -m "perf: add safe reflection delegate factory"
```

### Task 2: Army creation delegates

**Files:**
- Modify: `Code/core/lineage/AWArmyService.cs`
- Modify: `Code/core/lineage/RoyalGuardService.cs`

- [ ] **Step 1: Add the strong delegate type and cache after each existing `NewArmyObjectMethod` field**

In both services add `using AncientWarfare3.utils;`, then add:

```csharp
private delegate Army NewArmyObjectDelegate(ArmyManager pManager);

private static readonly NewArmyObjectDelegate NewArmyObjectInvoker =
    ReflectionDelegateFactory.TryCreate<NewArmyObjectDelegate>(
        NewArmyObjectMethod);
```

The `MethodInfo` field must remain declared first so static initialization resolves
the method before compiling the delegate.

- [ ] **Step 2: Route both army allocations through delegate-first helpers**

Add this helper to each service:

```csharp
private static Army CreateNativeArmyObject(ArmyManager pManager)
{
    if (NewArmyObjectInvoker != null)
        return NewArmyObjectInvoker(pManager);
    return NewArmyObjectMethod?.Invoke(pManager, null) as Army;
}
```

Replace only the existing allocation expression with:

```csharp
army = CreateNativeArmyObject(World.world.armies);
```

Do not alter creation guards, cleanup, role markers, or exception handling.

- [ ] **Step 3: Build the mod**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: 0 warnings and 0 errors.

- [ ] **Step 4: Confirm both fallbacks remain and no dynamic invocation was introduced**

```powershell
rg -n "NewArmyObjectInvoker|NewArmyObjectMethod\?\.Invoke|DynamicInvoke" `
  Code\core\lineage\AWArmyService.cs `
  Code\core\lineage\RoyalGuardService.cs
```

Expected: each service has an invoker and `MethodInfo.Invoke` fallback; no
`DynamicInvoke` output.

- [ ] **Step 5: Commit army integration**

```powershell
git add Code\core\lineage\AWArmyService.cs `
  Code\core\lineage\RoyalGuardService.cs
git commit -m "perf: cache native army creation delegates"
```

### Task 3: Hierarchical map selection cache

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`

- [ ] **Step 1: Add the bounded adapter cache**

Add `using AncientWarfare3.utils;` and these fields near the other static runtime
caches:

```csharp
private delegate bool SelectAndInspectInvoker(object pAsset, object pObject);
private static readonly Dictionary<Type, SelectAndInspectInvoker>
    SelectAndInspectByAssetType = new();
```

- [ ] **Step 2: Add signature-specific adapter construction with reflection fallback**

```csharp
private static SelectAndInspectInvoker ResolveSelectAndInspectInvoker(
    Type pAssetType)
{
    MethodInfo method = pAssetType?.GetMethod("selectAndInspect");
    if (method == null) return null;
    int count = method.GetParameters().Length;
    if (count == 4)
    {
        var call = ReflectionDelegateFactory.TryCreate<
            Action<object, object, bool, bool, bool>>(method);
        if (call != null)
            return (asset, value) =>
            {
                call(asset, value, false, false, false);
                return true;
            };
        return (asset, value) =>
        {
            method.Invoke(asset, new object[] { value, false, false, false });
            return true;
        };
    }
    if (count == 3)
    {
        var call = ReflectionDelegateFactory.TryCreate<
            Action<object, object, bool, bool>>(method);
        if (call != null)
            return (asset, value) =>
            {
                call(asset, value, false, false);
                return true;
            };
        return (asset, value) =>
        {
            method.Invoke(asset, new object[] { value, false, false });
            return true;
        };
    }
    if (count != 1) return null;
    var single = ReflectionDelegateFactory.TryCreate<Action<object, object>>(
        method);
    if (single != null)
        return (asset, value) => { single(asset, value); return true; };
    return (asset, value) =>
    {
        method.Invoke(asset, new[] { value });
        return true;
    };
}
```

- [ ] **Step 3: Replace repeated method discovery in `TrySelectAndInspect`**

After resolving `asset`, use its type as the bounded cache key:

```csharp
if (asset == null) return false;
Type assetType = asset.GetType();
if (!SelectAndInspectByAssetType.TryGetValue(assetType,
        out SelectAndInspectInvoker invoke))
{
    invoke = ResolveSelectAndInspectInvoker(assetType);
    SelectAndInspectByAssetType[assetType] = invoke;
}
return invoke != null && invoke(asset, pNanoObject);
```

Delete only the old local `GetMethod`, parameter-count branch, and direct calls.
Keep the outer `try/catch` and `false` return behavior.

- [ ] **Step 4: Build and inspect the map integration**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
rg -n "SelectAndInspectByAssetType|ResolveSelectAndInspectInvoker|DynamicInvoke|method\.Invoke" `
  Code\core\policy\HierarchicalVassalMapModeService.cs
```

Expected: build has 0 warnings and 0 errors; cache and reflection fallbacks are
present; `DynamicInvoke` is absent.

- [ ] **Step 5: Commit map integration**

```powershell
git add Code\core\policy\HierarchicalVassalMapModeService.cs
git commit -m "perf: cache hierarchical map reflection delegates"
```

### Task 4: Full regression and compatibility gate

**Files:**
- Verify: `Code/utils/ReflectionDelegateFactory.cs`
- Verify: `Code/core/lineage/AWArmyService.cs`
- Verify: `Code/core/lineage/RoyalGuardService.cs`
- Verify: `Code/core/policy/HierarchicalVassalMapModeService.cs`

- [ ] **Step 1: Run all rule tests**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
```

Expected: `Rule tests passed.`

- [ ] **Step 2: Run the RTS adversarial simulation**

```powershell
dotnet run --project Tests\ArmyRtsAdversarialSimulation\ArmyRtsAdversarialSimulation.csproj
```

Expected: `PASS foundation seed=17 trace=64`.

- [ ] **Step 3: Build the net48 mod**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: 0 warnings and 0 errors.

- [ ] **Step 4: Verify repository integrity and exact scope**

```powershell
git diff --check HEAD~3..HEAD
git status --short --branch
rg -n "DynamicInvoke" Code
```

Expected: no diff errors, no uncommitted production changes, and no
`DynamicInvoke` usage.
