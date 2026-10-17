using System;
using System.Linq;
using System.Reflection;

internal static class AllSuitesRunner
{
    internal static int Run()
    {
        var suites = typeof(AllSuitesRunner).Assembly.GetTypes()
            .Where(type => !type.IsNested && type.Name.EndsWith("Tests",
                StringComparison.Ordinal))
            .Select(type => type.GetMethod("Run",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null))
            .Where(method => method != null &&
                method.ReturnType == typeof(void) && !method.IsGenericMethod)
            .OrderBy(method => method.DeclaringType.FullName,
                StringComparer.Ordinal).ToArray();
        int failures = 0;
        foreach (MethodInfo suite in suites)
        {
            try
            {
                suite.Invoke(null, null);
                Console.WriteLine("PASS " + suite.DeclaringType.FullName);
            }
            catch (Exception error)
            {
                failures++;
                Exception cause = error is TargetInvocationException &&
                    error.InnerException != null ? error.InnerException : error;
                Console.Error.WriteLine("FAIL " + suite.DeclaringType.FullName +
                    ": " + cause);
            }
        }
        Console.WriteLine($"Suites: {suites.Length}, passed: {suites.Length - failures}, failed: {failures}");
        return suites.Length > 0 && failures == 0 ? 0 : 1;
    }
}
