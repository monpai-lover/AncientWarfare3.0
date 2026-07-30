using System;
using System.IO;
using AncientWarfare3.core.lineage;

public static class NobleHeirPregnancyRulesTests
{
    public static int Main()
    {
        try
        {
            Equal(50f, NobleHeirPregnancyRules.ResolvePregnancyDuration(
                    45f, true, true, true, false),
                "eligible mother receives ten-month pregnancy");
            Equal(50f, NobleHeirPregnancyRules.ResolvePregnancyDuration(
                    45f, true, true, false, true),
                "eligible father makes the couple eligible");
            Equal(45f, NobleHeirPregnancyRules.ResolvePregnancyDuration(
                    45f, true, true, false, false),
                "ordinary civilian retains vanilla duration");
            Equal(45f, NobleHeirPregnancyRules.ResolvePregnancyDuration(
                    45f, false, true, true, true),
                "non-pregnancy status duration is untouched");
            Equal(45f, NobleHeirPregnancyRules.ResolvePregnancyDuration(
                    45f, true, false, true, false),
                "miracle pregnancy without a partner is untouched");

            Equal(true, NobleHeirPregnancyRules.ShouldCreateRetryRequest(
                    true, true, false, false),
                "all-female completed delivery creates one retry");
            Equal(false, NobleHeirPregnancyRules.ShouldCreateRetryRequest(
                    true, true, false, true),
                "duplicate delivery callback cannot duplicate retry");
            Equal(false, NobleHeirPregnancyRules.ShouldCreateRetryRequest(
                    true, true, true, false),
                "delivery containing a living son clears retry");
            Equal(false, NobleHeirPregnancyRules.ShouldCreateRetryRequest(
                    false, true, false, false),
                "unmanaged vanilla delivery cannot create retry");

            Retry(NobleHeirRetryDisposition.Start,
                authority: true, nextCycle: true, motherAlive: true,
                nobleCouple: true, livingSon: false, partnerReady: true,
                pregnancyRemoved: true, adult: true, breedingAge: true,
                fertile: true, nutrition: true, citySafe: true,
                offspringRoom: true, offspringBypass: false,
                metaRoom: true, worldLaw: true,
                "valid pending retry starts");
            Retry(NobleHeirRetryDisposition.Wait,
                true, false, true, true, false, true, true, true, true,
                true, true, true, true, false, true, true,
                "retry waits for the next authority cycle");
            Retry(NobleHeirRetryDisposition.Wait,
                true, true, true, true, false, true, false, true, true,
                true, true, true, true, false, true, true,
                "retry waits until old pregnancy status is removed");
            Retry(NobleHeirRetryDisposition.Wait,
                false, true, true, true, false, true, true, true, true,
                true, true, true, true, false, true, true,
                "multiplayer replica cannot advance pregnancy");
            Retry(NobleHeirRetryDisposition.Wait,
                true, true, true, true, false, false, true, true, true,
                true, true, true, true, false, true, true,
                "missing or dead spouse keeps retry pending");
            Retry(NobleHeirRetryDisposition.Wait,
                true, true, true, true, false, true, true, true, true,
                false, true, true, false, true, true, true,
                "infertility blocks retry without bypass");
            Retry(NobleHeirRetryDisposition.Wait,
                true, true, true, true, false, true, true, true, true,
                true, false, true, false, true, true, true,
                "hunger blocks retry without bypass");
            Retry(NobleHeirRetryDisposition.Wait,
                true, true, true, true, false, true, true, true, true,
                true, true, false, false, true, true, true,
                "unsafe city blocks retry without bypass");
            Retry(NobleHeirRetryDisposition.Wait,
                true, true, true, true, false, true, true, true, true,
                true, true, true, false, false, true, true,
                "offspring limit blocks retry without bypass");
            Retry(NobleHeirRetryDisposition.Start,
                true, true, true, true, false, true, true, true, true,
                true, true, true, false, true, true, true,
                "qualified no-son line bypasses personal offspring limit");
            Retry(NobleHeirRetryDisposition.Wait,
                true, true, true, true, false, true, true, true, true,
                true, true, true, false, false, true, true,
                "ordinary noble remains blocked by personal offspring limit");
            Retry(NobleHeirRetryDisposition.Wait,
                true, true, true, true, false, true, true, true, true,
                true, true, true, false, true, false, true,
                "meta population limit remains authoritative");
            Retry(NobleHeirRetryDisposition.Clear,
                true, true, false, true, false, true, true, true, true,
                true, true, true, false, true, true, true,
                "dead mother clears stale retry");
            Retry(NobleHeirRetryDisposition.Clear,
                true, true, true, false, false, true, true, true, true,
                true, true, true, false, true, true, true,
                "lost noble identity clears retry");
            Retry(NobleHeirRetryDisposition.Clear,
                true, true, true, true, true, true, true, true, true,
                true, true, true, false, true, true, true,
                "existing living son completes retry loop");

            SourceGuards();
            Console.WriteLine("Noble heir pregnancy rule tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Retry(NobleHeirRetryDisposition expected,
        bool authority, bool nextCycle, bool motherAlive, bool nobleCouple,
        bool livingSon, bool partnerReady, bool pregnancyRemoved, bool adult,
        bool breedingAge, bool fertile, bool nutrition, bool citySafe,
        bool offspringRoom, bool offspringBypass, bool metaRoom,
        bool worldLaw, string name)
    {
        Equal(expected, NobleHeirPregnancyRules.EvaluateRetry(
            authority, nextCycle, motherAlive, nobleCouple, livingSon,
            partnerReady, pregnancyRemoved, adult, breedingAge, fertile,
            nutrition, citySafe, offspringRoom, offspringBypass, metaRoom,
            worldLaw), name);
    }

    private static void SourceGuards()
    {
        string service = File.ReadAllText(Path.Combine("Code", "core",
            "lineage", "NobleHeirPregnancyService.cs"));
        string patch = File.ReadAllText(Path.Combine("Code", "patch",
            "AW_NobleHeirPregnancyPatch.cs"));
        string keys = File.ReadAllText(Path.Combine("Code", "core",
            "lineage", "LineageKeys.cs"));
        string authority = File.ReadAllText(Path.Combine("Code", "core",
            "performance", "AWAuthorityCycleService.cs"));

        Contains(keys, "DYNASTIC_HEIR_RETRY_PENDING",
            "retry flag persists in ActorData");
        Contains(keys, "DYNASTIC_HEIR_RETRY_FATHER_ID",
            "retry father persists in ActorData");
        Contains(keys, "DYNASTIC_HEIR_RETRY_REQUEST_TIME",
            "retry request time persists in ActorData");
        Contains(patch, "BaseSimObject), \"addStatusEffect\"",
            "pregnancy start hook is installed");
        Contains(patch, "BabyMaker.makeBabyFromPregnancy",
            "whole-delivery completion hook is installed");
        Contains(patch, "BabyMaker.startMiracleBirth",
            "miracle pregnancy is explicitly excluded");
        Contains(patch, "BabyMaker.startSoulborneBirth",
            "soulborne pregnancy is explicitly excluded");
        Contains(patch, "NonSexualPregnancyDepth",
            "non-sexual pregnancy exclusion is scoped and reentrant");
        Contains(patch, "ActorManager.loadObject",
            "saved pending actors are restored lazily while loading");
        string birthPatch = File.ReadAllText(Path.Combine("Code", "patch",
            "AW_DynasticReproductionPatch.cs"));
        Contains(birthPatch, "DynasticLivingSonIndexService.OnChildBorn",
            "living-son index updates after BabyMaker finalizes child sex");
        Contains(birthPatch, "BabyHelper), nameof(BabyHelper.canMakeBabies)",
            "vanilla pregnancy gate receives a targeted transpiler");
        Contains(birthPatch, "ReachedPersonalOffspringLimit",
            "only the personal offspring gate is replaced");
        Equal(false, birthPatch.Contains("stats[\"offspring\"]"),
            "the patch never mutates the vanilla offspring stat");
        Contains(service, "Queue<long>", "retry processing uses a queue");
        Contains(service, "HashSet<long>", "retry queue is deduplicated");
        Contains(service, "MaxRetriesPerCycle",
            "retry processing has a fixed cycle budget");
        Contains(service, "AW3MultiplayerReplicaScope.IsReplicaSession",
            "replicas cannot create or advance pregnancies");
        Contains(service, "currentPartner.canBreed()",
            "retry preserves the original partner breeding gate");
        Contains(service, "pMother.isFighting()",
            "retry preserves the original actor safety gate");
        Contains(authority, "NobleHeirPregnancyService.ProcessAuthorityCycle",
            "retry runs on the authoritative simulation cycle");
        Contains(authority, "NobleHeirPregnancyService.Reset",
            "world reset clears runtime pregnancy indexes");
        Equal(false, service.Contains("World.world.units"),
            "pregnancy retry never scans every actor");
    }

    private static void Contains(string source, string needle, string name)
    {
        Equal(true, source.Contains(needle), name);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " +
                                                expected + ", got " + actual);
    }
}
