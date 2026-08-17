using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public sealed class CustomCourtInstanceService
    {
        private readonly IDictionary<string, CustomCourtInstance> _instances =
            new Dictionary<string, CustomCourtInstance>(StringComparer.Ordinal);

        public bool TryGet(string kingdomId, out CustomCourtInstance instance)
        {
            if (kingdomId == null)
            {
                instance = null;
                return false;
            }
            return _instances.TryGetValue(kingdomId, out instance);
        }

        public bool Save(CustomCourtInstance instance)
        {
            if (instance == null || !CustomCourtInstanceRules.IsValidKingdomId(
                    instance.KingdomId) ||
                !CustomCourtTemplateRules.IsValidTemplateId(instance.TemplateId) ||
                instance.ResolvedSnapshot == null ||
                CustomCourtTemplateRules.Validate(instance.ResolvedSnapshot) !=
                    CustomCourtTemplateValidationError.None)
                return false;
            _instances[instance.KingdomId] = instance;
            return true;
        }

        public bool Remove(string kingdomId)
        {
            return kingdomId != null && _instances.Remove(kingdomId);
        }
    }
}
