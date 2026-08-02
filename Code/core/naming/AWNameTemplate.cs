using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AncientWarfare3.core.naming
{
    public sealed class AWGeneratedName
    {
        public static readonly AWGeneratedName Empty = new AWGeneratedName(
            string.Empty, new Dictionary<string, string>());

        public AWGeneratedName(string pName,
            IReadOnlyDictionary<string, string> pComponents)
        {
            Name = pName ?? string.Empty;
            Components = pComponents ??
                new Dictionary<string, string>();
        }

        public string Name { get; }
        public IReadOnlyDictionary<string, string> Components { get; }
    }

    public sealed class AWNameTemplate
    {
        private readonly Atom[] _atoms;
        private readonly Atom[] _tagOrder;
        private readonly string[] _requiredParameters;

        private AWNameTemplate(string pFormat, float pWeight)
        {
            RawFormat = pFormat ?? string.Empty;
            Weight = pWeight;
            _atoms = Parse(RawFormat);
            _tagOrder = SortTaggedAtoms(_atoms, RawFormat);
            var tags = new HashSet<string>(_tagOrder.Select(pAtom => pAtom.Tag),
                StringComparer.Ordinal);
            _requiredParameters = _atoms
                .SelectMany(pAtom => pAtom.ParameterKeys)
                .Where(pKey => !tags.Contains(pKey))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public string RawFormat { get; }

        public float Weight { get; }

        public static AWNameTemplate Create(string pFormat, float pWeight = 1f)
        {
            return new AWNameTemplate(pFormat, pWeight);
        }

        public IReadOnlyDictionary<string, string> GetParametersToFill()
        {
            return _requiredParameters.ToDictionary(pKey => pKey,
                _ => string.Empty, StringComparer.Ordinal);
        }

        public string GenerateName(AWNameGenerationContext pContext,
            AWWordLibraryManager pLibraries)
        {
            return GenerateIdentity(pContext, pLibraries).Name;
        }

        public AWGeneratedName GenerateIdentity(
            AWNameGenerationContext pContext,
            AWWordLibraryManager pLibraries)
        {
            if (pContext == null || pLibraries == null)
                return AWGeneratedName.Empty;
            AWDeterministicNameRandom random = pContext.CreateRandom();
            return GenerateIdentity(pContext, pLibraries, ref random);
        }

        internal string GenerateName(AWNameGenerationContext pContext,
            AWWordLibraryManager pLibraries,
            ref AWDeterministicNameRandom pRandom)
        {
            return GenerateIdentity(pContext, pLibraries, ref pRandom).Name;
        }

        internal AWGeneratedName GenerateIdentity(
            AWNameGenerationContext pContext,
            AWWordLibraryManager pLibraries,
            ref AWDeterministicNameRandom pRandom)
        {
            Dictionary<string, string> values =
                pContext.CreateWorkingParameters();

            foreach (Atom atom in _tagOrder)
            {
                if (values.TryGetValue(atom.Tag, out string existing) &&
                    !string.IsNullOrEmpty(existing))
                    continue;
                if (!TryEvaluate(atom, values, pContext, pLibraries,
                        ref pRandom, out string generated))
                    return AWGeneratedName.Empty;
                values[atom.Tag] = generated;
            }

            var builder = new StringBuilder();
            foreach (Atom atom in _atoms)
            {
                if (!string.IsNullOrEmpty(atom.Tag))
                {
                    if (!values.TryGetValue(atom.Tag, out string taggedValue))
                        return AWGeneratedName.Empty;
                    builder.Append(taggedValue);
                    continue;
                }

                if (!TryEvaluate(atom, values, pContext, pLibraries,
                        ref pRandom, out string value))
                    return AWGeneratedName.Empty;
                builder.Append(value);
            }
            var components = new Dictionary<string, string>(
                StringComparer.Ordinal);
            foreach (Atom atom in _tagOrder)
                if (values.TryGetValue(atom.Tag, out string component))
                    components[atom.Tag] = component ?? string.Empty;
            return new AWGeneratedName(builder.ToString(), components);
        }

        private static bool TryEvaluate(Atom pAtom,
            Dictionary<string, string> pValues,
            AWNameGenerationContext pContext,
            AWWordLibraryManager pLibraries,
            ref AWDeterministicNameRandom pRandom, out string pValue)
        {
            if (pAtom.Type == AtomType.RawText)
            {
                pValue = pAtom.Pattern;
                return true;
            }

            if (!TryFillPattern(pAtom, pValues, pContext,
                    out string filled))
            {
                pValue = string.Empty;
                return false;
            }

            if (pAtom.Type == AtomType.Parameter)
            {
                pValue = filled;
                return !string.IsNullOrEmpty(filled);
            }

            if (string.IsNullOrEmpty(filled))
            {
                pValue = string.Empty;
                return !pAtom.AllParametersRequired;
            }

            if (pLibraries.TryPick(filled, ref pRandom, out pValue))
                return true;

            pValue = string.Empty;
            return !pAtom.AllParametersRequired;
        }

        private static bool TryFillPattern(Atom pAtom,
            Dictionary<string, string> pValues,
            AWNameGenerationContext pContext, out string pFilled)
        {
            if (pAtom.ParameterKeys.Length == 0)
            {
                pFilled = pAtom.Pattern;
                return true;
            }

            var values = new object[pAtom.ParameterKeys.Length];
            for (int i = 0; i < pAtom.ParameterKeys.Length; i++)
            {
                string key = pAtom.ParameterKeys[i];
                if (!pValues.TryGetValue(key, out string value) &&
                    !pContext.TryGetGlobal(key, out value))
                {
                    if (pAtom.AllParametersRequired)
                    {
                        pFilled = string.Empty;
                        return false;
                    }
                    value = string.Empty;
                }
                values[i] = value ?? string.Empty;
            }

            try
            {
                pFilled = string.Format(CultureInfo.InvariantCulture,
                    pAtom.Pattern, values);
                return true;
            }
            catch (FormatException)
            {
                pFilled = string.Empty;
                return false;
            }
        }

        private static Atom[] Parse(string pFormat)
        {
            var atoms = new List<Atom>();
            int index = 0;
            while (index < pFormat.Length)
            {
                char current = pFormat[index];
                if (current == '{' || current == '<')
                {
                    char close = current == '{' ? '}' : '>';
                    int end = pFormat.IndexOf(close, index + 1);
                    if (end < 0)
                        throw Invalid(pFormat, index,
                            $"missing closing '{close}'");
                    string body = pFormat.Substring(index + 1,
                        end - index - 1);
                    atoms.Add(ParseWordAtom(body, current == '<', pFormat,
                        index));
                    index = end + 1;
                    continue;
                }

                if (current == '$')
                {
                    int end = pFormat.IndexOf('$', index + 1);
                    if (end < 0)
                        throw Invalid(pFormat, index,
                            "missing closing '$'");
                    string key = pFormat.Substring(index + 1,
                        end - index - 1);
                    if (string.IsNullOrWhiteSpace(key))
                        throw Invalid(pFormat, index,
                            "parameter name cannot be empty");
                    atoms.Add(Atom.Parameter(key));
                    index = end + 1;
                    continue;
                }

                if (current == '#')
                {
                    int end = pFormat.IndexOf('#', index + 1);
                    if (end < 0)
                        throw Invalid(pFormat, index,
                            "missing closing '#'");
                    atoms.Add(Atom.Raw(pFormat.Substring(index + 1,
                        end - index - 1)));
                    index = end + 1;
                    continue;
                }

                if (current == '}' || current == '>')
                    throw new AWInvalidNameTemplateException(current, index,
                        pFormat, "missing opening bracket");

                int rawEnd = index + 1;
                while (rawEnd < pFormat.Length &&
                       "{<$#}>".IndexOf(pFormat[rawEnd]) < 0)
                    rawEnd++;
                atoms.Add(Atom.Raw(pFormat.Substring(index,
                    rawEnd - index)));
                index = rawEnd;
            }
            return atoms.ToArray();
        }

        private static Atom ParseWordAtom(string pBody, bool pRequired,
            string pRawFormat, int pOffset)
        {
            int tagSeparator = FindTagSeparator(pBody);
            string patternSource = tagSeparator < 0
                ? pBody
                : pBody.Substring(0, tagSeparator);
            string tag = tagSeparator < 0
                ? string.Empty
                : pBody.Substring(tagSeparator + 1).Trim();
            if (tagSeparator >= 0 && tag.Length == 0)
                throw Invalid(pRawFormat, pOffset,
                    "tag name cannot be empty");

            ParsePattern(patternSource, pRawFormat, pOffset,
                out string pattern, out string[] keys);
            if (string.IsNullOrWhiteSpace(patternSource))
                throw Invalid(pRawFormat, pOffset,
                    "word library name cannot be empty");
            return Atom.Word(pattern, keys, tag, pRequired);
        }

        private static int FindTagSeparator(string pBody)
        {
            bool inParameter = false;
            for (int i = 0; i < pBody.Length; i++)
            {
                if (pBody[i] == '$')
                {
                    inParameter = !inParameter;
                    continue;
                }
                if (pBody[i] == ':' && !inParameter) return i;
            }
            if (inParameter)
                throw new AWInvalidNameTemplateException(
                    "Unclosed parameter in word-library atom.");
            return -1;
        }

        private static void ParsePattern(string pSource, string pRawFormat,
            int pOffset, out string pPattern, out string[] pKeys)
        {
            var pattern = new StringBuilder();
            var keys = new List<string>();
            for (int i = 0; i < pSource.Length; i++)
            {
                if (pSource[i] != '$')
                {
                    if (pSource[i] == '{' || pSource[i] == '}')
                        pattern.Append(pSource[i]);
                    pattern.Append(pSource[i]);
                    continue;
                }

                int end = pSource.IndexOf('$', i + 1);
                if (end < 0)
                    throw Invalid(pRawFormat, pOffset + i,
                        "missing closing '$' in word-library atom");
                string key = pSource.Substring(i + 1, end - i - 1);
                if (string.IsNullOrWhiteSpace(key))
                    throw Invalid(pRawFormat, pOffset + i,
                        "parameter name cannot be empty");
                pattern.Append('{').Append(keys.Count).Append('}');
                keys.Add(key);
                i = end;
            }
            pPattern = pattern.ToString();
            pKeys = keys.ToArray();
        }

        private static Atom[] SortTaggedAtoms(Atom[] pAtoms,
            string pRawFormat)
        {
            var tagged = new Dictionary<string, Atom>(StringComparer.Ordinal);
            foreach (Atom atom in pAtoms)
            {
                if (string.IsNullOrEmpty(atom.Tag)) continue;
                if (tagged.ContainsKey(atom.Tag))
                    throw new AWInvalidNameTemplateException(
                        $"Duplicate tag '{atom.Tag}' in '{pRawFormat}'.");
                tagged.Add(atom.Tag, atom);
            }

            var result = new List<Atom>();
            var states = new Dictionary<string, byte>(StringComparer.Ordinal);
            foreach (string tag in tagged.Keys.OrderBy(pTag => pTag,
                         StringComparer.Ordinal))
                Visit(tag, tagged, states, result, pRawFormat);
            return result.ToArray();
        }

        private static void Visit(string pTag,
            Dictionary<string, Atom> pTagged,
            Dictionary<string, byte> pStates, List<Atom> pResult,
            string pRawFormat)
        {
            if (pStates.TryGetValue(pTag, out byte state))
            {
                if (state == 2) return;
                if (state == 1)
                    throw new AWInvalidNameTemplateException(
                        $"Cyclic tag '{pTag}' in '{pRawFormat}'.");
            }

            pStates[pTag] = 1;
            Atom atom = pTagged[pTag];
            foreach (string dependency in atom.ParameterKeys
                         .Where(pTagged.ContainsKey)
                         .Distinct(StringComparer.Ordinal))
                Visit(dependency, pTagged, pStates, pResult, pRawFormat);
            pStates[pTag] = 2;
            pResult.Add(atom);
        }

        private static AWInvalidNameTemplateException Invalid(string pFormat,
            int pIndex, string pReason)
        {
            return new AWInvalidNameTemplateException(
                $"Invalid name template at {pIndex} in \"{pFormat}\": {pReason}.");
        }

        private enum AtomType
        {
            WordLibrary,
            RawText,
            Parameter
        }

        private sealed class Atom
        {
            private Atom(AtomType pType, string pPattern,
                string[] pParameterKeys, string pTag,
                bool pAllParametersRequired)
            {
                Type = pType;
                Pattern = pPattern ?? string.Empty;
                ParameterKeys = pParameterKeys ?? Array.Empty<string>();
                Tag = pTag ?? string.Empty;
                AllParametersRequired = pAllParametersRequired;
            }

            public AtomType Type { get; }
            public string Pattern { get; }
            public string[] ParameterKeys { get; }
            public string Tag { get; }
            public bool AllParametersRequired { get; }

            public static Atom Word(string pPattern, string[] pKeys,
                string pTag, bool pRequired)
            {
                return new Atom(AtomType.WordLibrary, pPattern, pKeys, pTag,
                    pRequired);
            }

            public static Atom Raw(string pText)
            {
                return new Atom(AtomType.RawText, pText,
                    Array.Empty<string>(), string.Empty, false);
            }

            public static Atom Parameter(string pKey)
            {
                return new Atom(AtomType.Parameter, "{0}",
                    new[] { pKey }, string.Empty, true);
            }
        }
    }
}
