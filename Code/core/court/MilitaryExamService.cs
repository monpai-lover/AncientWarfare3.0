using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class MilitaryExamService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (DB == null || pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral() || !pKingdom.isCiv()) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.CIVIL_SERVICE_EXAM_ANCHOR_YEAR,
                out int anchorYear, -1);
            if (anchorYear < 1)
            {
                pKingdom.data.set(LineageKeys.CIVIL_SERVICE_EXAM_ANCHOR_YEAR,
                    ExamCycleRules.FirstOpeningYear(year));
                return;
            }
            if (!ExamCycleRules.IsCycleYear(year, anchorYear)) return;

            string mode = ExamCycleRules.ResolveMode(
                MandateService.IsMandateKingdom(pKingdom),
                KingdomTitleService.IsEmperor(pKingdom));
            if (!TryResolveDemand(pKingdom, out int generalTarget,
                    out int localTarget, out int generalVacancies,
                    out int localVacancies, out int reserveTarget)) return;

            long openDay = DueWorldDay(year, pKingdom.id);
            var session = new MilitaryExamSessionRecord
            {
                KingdomId = pKingdom.id,
                KingdomName = pKingdom.name ?? "",
                Mode = mode,
                CycleYear = year,
                OpenWorldDay = openDay,
                NextDueWorldDay = openDay,
                HostRulerId = pKingdom.king?.data?.id ?? -1L,
                CandidateCursor = 0,
                GeneralTarget = generalTarget,
                LocalTarget = localTarget,
                GeneralVacancies = generalVacancies,
                LocalVacancies = localVacancies,
                ReserveTarget = reserveTarget,
                AdmissionQuota = generalVacancies + localVacancies + reserveTarget,
                UpdatedTime = LineageService.CurTime()
            };
            if (MilitaryExamPersistence.TryCreateSession(DB, session))
                PopulateCandidates(pKingdom, session);
        }

        private static void PopulateCandidates(Kingdom pKingdom,
            MilitaryExamSessionRecord pSession)
        {
            if (pKingdom?.data == null || pSession == null || pSession.Id < 0L) return;
            int target = Math.Min(64, Math.Max(0, pSession.ReserveTarget));
            if (target == 0) return;
            var candidates = new List<MilitaryExamCandidateRecord>();
            try
            {
                foreach (Actor actor in pKingdom.getUnits())
                {
                    if (actor?.data == null || actor.kingdom != pKingdom ||
                        actor.isRekt() || !actor.isAlive() || !actor.isSexMale() ||
                        !actor.isAdult() || actor.getAge() >= MilitaryExamRules.RetirementAge ||
                        actor.isKing() || HeirService.PeekRegisteredHeir(pKingdom) == actor ||
                        FeudatoryService.IsActivePrince(actor) ||
                        SlaveService.IsSlave(actor) || GeneralService.IsGeneral(actor) ||
                        IsArmyCaptain(actor)) continue;
                    int warfare = Stat(actor, "warfare");
                    int damage = Stat(actor, "damage");
                    int strength = Stat(actor, "strength");
                    int speed = Stat(actor, "speed");
                    int diplomacy = Stat(actor, "diplomacy");
                    int merit = Math.Max(0, Math.Min(100, GeneralService.GetMerit(actor)));
                    candidates.Add(new MilitaryExamCandidateRecord
                    {
                        SessionId = pSession.Id,
                        KingdomId = pKingdom.id,
                        ActorId = actor.data.id,
                        ActorName = actor.getName() ?? "",
                        HomeCityId = actor.city?.data?.id ?? -1L,
                        HomeCityName = actor.city?.data?.name ?? "",
                        AgeSnapshot = Math.Max(0, (int)Math.Round((double)actor.getAge())),
                        WarfareScore = warfare,
                        DamageScore = damage,
                        StrengthScore = strength,
                        SpeedScore = speed,
                        DiplomacyScore = diplomacy,
                        MilitaryMeritScore = merit,
                        TotalScore = MilitaryExamRules.Score(warfare, damage, strength,
                            speed, diplomacy, merit),
                        UpdatedTime = LineageService.CurTime()
                    });
                }
            }
            catch { return; }
            candidates.Sort((left, right) =>
            {
                int score = right.TotalScore.CompareTo(left.TotalScore);
                return score != 0 ? score : left.ActorId.CompareTo(right.ActorId);
            });
            if (candidates.Count > target) candidates.RemoveRange(target,
                candidates.Count - target);
            MilitaryExamPersistence.InsertCandidates(DB, candidates);
        }

        private static bool IsArmyCaptain(Actor pActor)
        {
            try { if (pActor.isArmyGroupLeader()) return true; } catch { }
            try { return pActor.hasArmy() && pActor.army?.getCaptain() == pActor; }
            catch { return false; }
        }

        private static int Stat(Actor pActor, string pKey)
        {
            try
            {
                float value = pActor.stats?[pKey] ?? 0f;
                if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
                return Math.Max(0, Math.Min(100, (int)Math.Round(value)));
            }
            catch { return 0; }
        }

        private static bool TryResolveDemand(Kingdom pKingdom,
            out int generalTarget, out int localTarget,
            out int generalVacancies, out int localVacancies,
            out int reserveTarget)
        {
            generalTarget = localTarget = generalVacancies = localVacancies = reserveTarget = 0;
            int garrisonCities = 0;
            int uncommandedArmies = 0;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() || city.kingdom != pKingdom) continue;
                    if (!city.hasArmy()) continue;
                    garrisonCities++;
                    Army army = null;
                    try { army = city.getArmy(); } catch { }
                    Actor captain = null;
                    try { captain = army?.getCaptain(); } catch { }
                    if (captain?.data == null || captain.isRekt() || !captain.isAlive())
                        uncommandedArmies++;
                }
            }
            catch { return false; }

            int activeGenerals = 0;
            try { activeGenerals = GeneralService.GetActiveGenerals(pKingdom).Count; }
            catch { }
            int strategicArmies = Math.Max(0, activeGenerals);
            generalTarget = MilitaryEstablishmentRules.GeneralTarget(
                strategicArmies, 0, pKingdom.king != null, 0);
            localTarget = MilitaryEstablishmentRules.LocalTarget(
                garrisonCities, uncommandedArmies, 0, 0);
            int lockedGeneral = CountLocked(pKingdom.id, "general");
            int lockedLocal = CountLocked(pKingdom.id, "local");
            generalVacancies = MilitaryEstablishmentRules.Vacancies(
                generalTarget, activeGenerals, lockedGeneral);
            localVacancies = MilitaryEstablishmentRules.Vacancies(
                localTarget, 0, lockedLocal);
            reserveTarget = (generalTarget + localTarget) > 0
                ? MilitaryExamRules.CandidateReserveTarget(
                    generalVacancies, localVacancies, hasMilitaryOrganization: true)
                : 0;
            return true;
        }

        private static int CountLocked(long pKingdomId, string pTier)
        {
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT COUNT(1) FROM " +
                    MilitaryOfficerRecordTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom AND TIER=@tier AND ACTIVE=1";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@tier", pTier);
                return Convert.ToInt32(command.ExecuteScalar());
            }
            catch { return 0; }
        }

        private static long DueWorldDay(int pYear, long pKingdomId)
        {
            long day = (long)Math.Max(0, pYear) * 360L +
                       Math.Abs(pKingdomId % 30L);
            return day;
        }
    }
}
