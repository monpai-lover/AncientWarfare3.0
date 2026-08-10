namespace AncientWarfare3.core.naming
{
    public enum NamingProfileId
    {
        None,
        Xia,
        Monkey,
        NativeSinitic,
        OrcNomadic,
        Western
    }

    public static class AWNamingProfileRules
    {
        public static NamingProfileId Resolve(bool biologicalXia,
            bool civilizedMonkey, bool orc, bool civilized, bool valid)
        {
            return Resolve(biologicalXia, civilizedMonkey,
                nativeSinitic: false, orc, civilized, valid);
        }

        public static NamingProfileId Resolve(bool biologicalXia,
            bool civilizedMonkey, bool nativeSinitic, bool orc,
            bool civilized, bool valid)
        {
            if (!valid || !civilized)
                return NamingProfileId.None;
            if (biologicalXia)
                return NamingProfileId.Xia;
            if (civilizedMonkey)
                return NamingProfileId.Monkey;
            if (nativeSinitic)
                return NamingProfileId.NativeSinitic;
            return orc
                ? NamingProfileId.OrcNomadic
                : NamingProfileId.Western;
        }
    }
}
