using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public sealed class CustomCourtApplicationService
    {
        private readonly CustomCourtInstanceService _instances;

        public CustomCourtApplicationService(CustomCourtInstanceService instances)
        {
            _instances = instances;
        }

        public bool TryBuildInstance(string kingdomId,
            CustomCourtTemplate template,
            CustomCourtInstance current,
            IReadOnlyDictionary<string, long> incumbents,
            out CustomCourtInstance next)
        {
            next = null;
            if (!CustomCourtInstanceRules.IsValidKingdomId(kingdomId) ||
                template == null || CustomCourtTemplateRules.Validate(template) !=
                    CustomCourtTemplateValidationError.None)
                return false;

            CustomCourtTemplate snapshot =
                CustomCourtTemplateJsonCodec.Normalize(template);
            next = new CustomCourtInstance
            {
                KingdomId = kingdomId,
                TemplateId = snapshot.Id,
                TemplateRevision = snapshot.Revision,
                TemplateHash = CustomCourtTemplateJsonCodec.Hash(snapshot),
                ResolvedSnapshot = snapshot
            };

            if (current?.ResolvedSnapshot?.Offices != null)
            {
                foreach (CustomCourtOffice oldOffice in
                    current.ResolvedSnapshot.Offices)
                {
                    if (oldOffice == null ||
                        CustomCourtApplicationRules.ContainsOffice(snapshot,
                            oldOffice.Id))
                        continue;
                    long incumbentId;
                    if (incumbents == null || !incumbents.TryGetValue(
                            oldOffice.Id, out incumbentId) || incumbentId < 0)
                        continue;
                    next.LegacyOffices.Add(new CustomCourtLegacyOffice
                    {
                        OfficeId = oldOffice.Id,
                        FormerName = oldOffice.Name?.Chinese ?? string.Empty,
                        RetiredRevision = snapshot.Revision
                    });
                }
            }
            return true;
        }

        public bool TryApply(string kingdomId, CustomCourtTemplate template,
            CustomCourtInstance current,
            IReadOnlyDictionary<string, long> incumbents)
        {
            CustomCourtInstance next;
            if (!TryBuildInstance(kingdomId, template, current, incumbents,
                    out next))
                return false;
            return _instances != null && _instances.Save(next);
        }
    }
}
