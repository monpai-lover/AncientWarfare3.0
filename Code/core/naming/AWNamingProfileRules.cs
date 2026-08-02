namespace AncientWarfare3.core.naming
{
    public enum NamingProfileId
    {
        None,
        Xia,
        Monkey,
        OrcNomadic,
        Western
    }

    public static class AWNamingProfileRules
    {
        public static NamingProfileId Resolve(bool biologicalXia,
            bool civilizedMonkey, bool orc, bool civilized, bool valid)
        {
            if (!valid || !civilized)
                return NamingProfileId.None;
            if (biologicalXia)
                return NamingProfileId.Xia;
            if (civilizedMonkey)
                return NamingProfileId.Monkey;
            return orc
                ? NamingProfileId.OrcNomadic
                : NamingProfileId.Western;
        }
    }
}
