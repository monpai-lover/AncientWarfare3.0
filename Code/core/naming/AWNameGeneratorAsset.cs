using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.naming
{
    public class AWNameGeneratorAsset
    {
        private readonly AWNameTemplate[] _templates;

        public AWNameGeneratorAsset(string pId,
            IEnumerable<AWNameTemplate> pTemplates,
            AWNameTemplate pDefaultTemplate = null,
            string pParameterGetter = "default")
        {
            Id = (pId ?? string.Empty).Trim();
            ParameterGetter = string.IsNullOrWhiteSpace(pParameterGetter)
                ? "default"
                : pParameterGetter.Trim();
            _templates = (pTemplates ?? Array.Empty<AWNameTemplate>())
                .Where(pTemplate => pTemplate != null)
                .ToArray();
            DefaultTemplate = pDefaultTemplate ??
                              AWNameTemplate.Create("#NO_NAME#", 1f);
        }

        public string Id { get; }

        public string ParameterGetter { get; }

        public AWNameTemplate DefaultTemplate { get; }

        public IReadOnlyList<AWNameTemplate> Templates => _templates;

        public virtual string GenerateName(AWNameGenerationContext pContext,
            AWWordLibraryManager pLibraries)
        {
            return GenerateIdentity(pContext, pLibraries).Name;
        }

        public virtual AWGeneratedName GenerateIdentity(
            AWNameGenerationContext pContext,
            AWWordLibraryManager pLibraries)
        {
            if (pContext == null || pLibraries == null)
                return AWGeneratedName.Empty;
            AWDeterministicNameRandom random = pContext.CreateRandom();
            var remaining = Enumerable.Range(0, _templates.Length)
                .Where(pIndex => _templates[pIndex].Weight > 0f)
                .ToList();

            int attempts = Math.Min(10, remaining.Count);
            while (attempts-- > 0 && remaining.Count > 0)
            {
                int position = SelectWeightedPosition(remaining, ref random);
                AWNameTemplate template = _templates[remaining[position]];
                remaining.RemoveAt(position);
                AWGeneratedName generated = template.GenerateIdentity(
                    pContext, pLibraries,
                    ref random);
                if (!string.IsNullOrEmpty(generated.Name)) return generated;
            }

            return DefaultTemplate.GenerateIdentity(pContext, pLibraries,
                ref random);
        }

        private int SelectWeightedPosition(IReadOnlyList<int> pRemaining,
            ref AWDeterministicNameRandom pRandom)
        {
            double total = 0d;
            for (int i = 0; i < pRemaining.Count; i++)
                total += Math.Max(0f, _templates[pRemaining[i]].Weight);
            if (total <= 0d) return pRandom.NextIndex(pRemaining.Count);

            double selected = pRandom.NextUnit() * total;
            for (int i = 0; i < pRemaining.Count; i++)
            {
                selected -= Math.Max(0f, _templates[pRemaining[i]].Weight);
                if (selected <= 0d) return i;
            }
            return pRemaining.Count - 1;
        }
    }
}
