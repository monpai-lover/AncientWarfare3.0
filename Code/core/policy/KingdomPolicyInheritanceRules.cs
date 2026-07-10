using AncientWarfare3.content.policies;

namespace AncientWarfare3.core.policy
{
    public static class KingdomPolicyInheritanceRules
    {
        public static string SanitizeClassStateForNewKingdom(string pSourceClass, string pDefaultClass)
        {
            return pSourceClass == KingdomPolicyDefs.ClassRepublic ? pDefaultClass : pSourceClass;
        }
    }
}
