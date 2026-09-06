namespace AncientWarfare3.core.lineage
{
    public static class VassalWarPermissionRules
    {
        public static bool CanDeclareWar(bool pAttackerIsVassal, bool pDefenderIsSuzerain,
            bool pSameRootSuzerain, bool pBlockInternalWar, string pWarType, out string pReason)
        {
            return CanDeclareWar(pAttackerIsVassal, false,
                pDefenderIsSuzerain, pSameRootSuzerain, pBlockInternalWar,
                pWarType, out pReason);
        }

        public static bool CanDeclareWar(bool pAttackerIsVassal,
            bool pDefenderIsSubject, bool pDefenderIsSuzerain,
            bool pSameRootSuzerain, bool pBlockInternalWar, string pWarType,
            out string pReason)
        {
            return CanDeclareWar(pAttackerIsVassal, pDefenderIsSubject,
                pDefenderIsSuzerain, pSameRootSuzerain,
                pBlockInternalWar, pWarType, VassalSubjectKind.Ordinary,
                out pReason);
        }

        public static bool CanDeclareWar(bool pAttackerIsVassal,
            bool pDefenderIsSubject, bool pDefenderIsSuzerain,
            bool pSameRootSuzerain, bool pBlockInternalWar, string pWarType,
            VassalSubjectKind pDefenderSubjectKind, out string pReason)
        {
            if (pDefenderIsSuzerain && pWarType == "independence_war")
            {
                pReason = "";
                return true;
            }

            _ = pSameRootSuzerain;
            _ = pBlockInternalWar;
            bool externalGovernorateTarget = !pAttackerIsVassal &&
                pDefenderIsSubject &&
                MilitaryGovernorateWarRules.
                    CanExternalRealmTargetGovernorate(pDefenderSubjectKind);
            if (pAttackerIsVassal ||
                pDefenderIsSubject && !externalGovernorateTarget)
            {
                pReason = "vassal_external_war_blocked";
                return false;
            }

            pReason = "";
            return true;
        }

        public static bool CanUseOrdinaryWarDecision(bool pSourceIsSubject,
            bool pTargetIsSubject)
        {
            return !pSourceIsSubject && !pTargetIsSubject;
        }

        public static bool CanUseOrdinaryWarDecision(bool pSourceIsSubject,
            bool pTargetIsSubject,
            VassalSubjectKind pTargetSubjectKind)
        {
            return !pSourceIsSubject &&
                   (!pTargetIsSubject ||
                    pTargetSubjectKind ==
                    VassalSubjectKind.MilitaryGovernorate);
        }

        public static bool CanCreateAlliance(bool pActorIsVassal, out string pReason)
        {
            if (!pActorIsVassal)
            {
                pReason = "";
                return true;
            }

            pReason = "vassal_no_alliance";
            return false;
        }

        public static bool CanUseAlliancePlot(bool initiatorIsVassal, bool targetIsVassal)
        {
            return !initiatorIsVassal && !targetIsVassal;
        }
    }
}
