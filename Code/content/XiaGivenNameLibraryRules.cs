using System;

namespace AncientWarfare3.content
{
    public static class XiaGivenNameLibraryRules
    {
        private static readonly string[] HanNames =
            ("邦,盈,恒,启,彻,弗陵,病已,奭,骜,欣,衎,婴,如意,长,肥,恢,建,旦,胥,髆,贺,据,进,弘," +
             "章,和,祜,保,缵,志,宏,协,秀,庄,炟,肇,隆,良,平,信,参,何,勃,绾,苍,亚夫,广,青," +
             "去病,迁,向,歆,雄,衡,固,超,宪,伦,融,邕,玄,彪,寿,宠,霸,敞,禹,恭,恽,延年," +
             "宽饶,汤,吉,福,光,丰,商,友,定,武,德,明,安,成,昭,宣,穆,景,元")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        public static string[] HanGivenNames()
        {
            return (string[])HanNames.Clone();
        }

        public static string HanGivenNamesCsv => string.Join(",", HanNames);
    }
}
