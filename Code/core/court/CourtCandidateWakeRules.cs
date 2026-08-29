namespace AncientWarfare3.core.court
{
    /// <summary>
    ///     什么样的「有人成年」值得唤醒一次补缺对账。
    ///
    ///     补缺发现是纯事件驱动的,但填补一次要走一遍全国名单。8k 人口下
    ///     <c>Actor.eventBecomeAdult</c> 是高频事件,而绝大多数新成年的人
    ///     连候选池的门都进不去 —— 唤醒了也只是把名单白扫一遍。
    ///
    ///     这里只用「成年当刻就能廉价读到、且此后不会变回去」的事实。四条
    ///     排除项全部来自
    ///     <see cref="LocalOfficialCandidateRules.CanEnter"/> 与
    ///     <c>LocalCourtAppointmentService.CanUseCandidateFacts</c> 的硬性前置:
    ///     后者要求 <c>isSexMale()</c>,前者排除 slave / king / heir。中央与
    ///     封国层另有 <see cref="CourtRules.CanHoldLayerOffice"/> 的男性要求,
    ///     所以四层都被这一条覆盖。
    ///
    ///     判错的代价是有界的:漏唤醒只会让这次任命推迟到下一个事件或
    ///     <c>CityBureauAnnualWorkService</c> 的年度 Request,不会永久空缺。
    /// </summary>
    internal static class CourtCandidateWakeRules
    {
        public static bool ShouldWakeForNewAdult(bool pMale, bool pSlave,
            bool pKing, bool pHeir)
        {
            return pMale && !pSlave && !pKing && !pHeir;
        }
    }
}
