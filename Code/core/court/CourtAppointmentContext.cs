namespace AncientWarfare3.core.court
{
    /// <summary>
    /// 一次任命判定里**与候选人无关**的那部分事实:王国有没有科举/九品、这个
    /// 席位是几品、是不是方镇主官。它只取决于 (王国, 层, 官职, 城)。
    ///
    /// 为什么要单独拎出来:<see cref="CivilServiceQualificationService
    /// .CanReceiveFormalCivilAppointment"/> 原来把这四问写在函数开头,于是候选
    /// 池里每一个人都要重算一遍。而这四问一个比一个贵 ——
    ///
    ///   HasExaminationSystem / HasNineRankSystem → 已完成技术串的逐项比对;
    ///   OfficeGradeForOffice(City 层)           → 自定义朝堂模板查表 + LINQ;
    ///   IsRegionalGovernorSeat                   → 解析城主官职 + 找所属方镇。
    ///
    /// 建候选表是「候选池 × (城, 官职) 对数」的双层循环,8k 存档上内层是两三千
    /// 人,于是这四问被重复了上百万次。把它们提到循环外算一次,结果完全相同 ——
    /// 建表期间王国政策、朝堂模板、方镇归属都不会变。
    /// </summary>
    internal readonly struct CourtAppointmentContext
    {
        /// <summary>false 表示没有预算,被调用方应当自己现算(老路径)。</summary>
        internal readonly bool Valid;
        internal readonly bool ExaminationSystem;
        internal readonly bool NineRankSystem;
        internal readonly int OfficeGrade;
        internal readonly bool RegionalGovernor;

        private CourtAppointmentContext(bool pExaminationSystem,
            bool pNineRankSystem, int pOfficeGrade, bool pRegionalGovernor)
        {
            Valid = true;
            ExaminationSystem = pExaminationSystem;
            NineRankSystem = pNineRankSystem;
            OfficeGrade = pOfficeGrade;
            RegionalGovernor = pRegionalGovernor;
        }

        internal static CourtAppointmentContext Build(Kingdom pKingdom,
            string pLayer, string pOfficeId, City pCity)
        {
            if (pKingdom?.data == null) return default;
            return new CourtAppointmentContext(
                CivilServiceQualificationService.HasExaminationSystem(pKingdom),
                CourtService.HasNineRankSystem(pKingdom),
                OfficialCareerStateService.OfficeGradeForOffice(pKingdom,
                    pLayer, pOfficeId, pCity),
                OfficialCareerStateService.IsRegionalGovernorSeat(pKingdom,
                    pLayer, pOfficeId, pCity));
        }
    }
}
