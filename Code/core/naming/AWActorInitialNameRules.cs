using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.naming
{
    public static class AWActorInitialNameRules
    {
        public static string ResolveGeneratedName(string pGeneratedName,
            IReadOnlyDictionary<string, string> pComponents,
            bool preserveFamilyIdentity = false)
        {
            string generated = (pGeneratedName ?? string.Empty).Trim();
            if (generated.Length == 0) return string.Empty;
            if (preserveFamilyIdentity) return generated;

            if (TryGetComponent(pComponents, "given_name", out string given))
                return given;

            string withoutIdentity = generated;
            RemoveFirst(ref withoutIdentity, pComponents, "family_name");
            RemoveFirst(ref withoutIdentity, pComponents, "middle_name");
            withoutIdentity = withoutIdentity.Trim();
            if (withoutIdentity.Length > 0) return withoutIdentity;

            return TryGetComponent(pComponents, "family_name",
                out string onlyComponent)
                ? onlyComponent
                : generated;
        }

        private static bool TryGetComponent(
            IReadOnlyDictionary<string, string> pComponents, string pKey,
            out string pValue)
        {
            pValue = string.Empty;
            if (pComponents == null ||
                !pComponents.TryGetValue(pKey, out string value) ||
                string.IsNullOrWhiteSpace(value))
                return false;
            pValue = value.Trim();
            return true;
        }

        private static void RemoveFirst(ref string pValue,
            IReadOnlyDictionary<string, string> pComponents, string pKey)
        {
            if (!TryGetComponent(pComponents, pKey, out string component))
                return;
            int index = pValue.IndexOf(component, StringComparison.Ordinal);
            if (index >= 0) pValue = pValue.Remove(index, component.Length);
        }
    }
}
