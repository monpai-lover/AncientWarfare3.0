using System.Reflection;
using AncientWarfare3.patch;
using UnityEngine;

internal static class Program
{
    private static int Main()
    {
        Action[] tests = { NullContainerUsesTextureFallback, MissingContainerUsesKnownFallback,
            FinalizerRecoversAfterContainerCreationFailure, BrokenFallbackPreservesOriginalException,
            ValidIdleSpriteIsPreserved, OtherSpeciesPassesThrough, NegativeHeadIndexIsRepaired,
            OversizedHeadIndexIsRepaired, ValidHeadIndexIsPreserved, MissingEquipmentUsesOrdinaryHead,
            MissingHelmetUsesOrdinaryHead, UnassignedHeadUsesDeterministicSelection,
            BabyHeadRemainsHidden, HanEmperorKeepsSpecialHead, MissingHanHeadUsesOrdinaryKingHead };
        int failed = 0;
        foreach (Action test in tests)
        {
            SpriteTextureLoader.Load = _ => Array.Empty<Sprite>();
            SpriteTextureLoader.LoadSingle = _ => null;
            try { test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + error); }
        }
        Console.WriteLine($"Actor sprite safety: {tests.Length - failed}/{tests.Length} passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void NullContainerUsesTextureFallback()
    {
        var expected = new Sprite();
        SpriteTextureLoader.Load = path => path == "actor" ? new[] { expected } : Array.Empty<Sprite>();
        Sprite actual = null;
        Equal(false, AW_ActorMainSpritePatch.CalculateMainSprite_Prefix(new Actor(), ref actual), "fallback handles frame");
        Equal(expected, actual, "actor texture is used without an animation container");
    }

    private static void MissingContainerUsesKnownFallback()
    {
        var expected = new Sprite();
        SpriteTextureLoader.Load = path => path == "xia/male_1" ? new[] { expected } : Array.Empty<Sprite>();
        Sprite actual = null;
        Equal(false, AW_ActorMainSpritePatch.CalculateMainSprite_Prefix(new Actor(), ref actual), "known fallback handles frame");
        Equal(expected, actual, "known race texture is used when actor texture is unavailable");
    }

    private static void FinalizerRecoversAfterContainerCreationFailure()
    {
        var expected = new Sprite();
        SpriteTextureLoader.Load = _ => new[] { expected };
        var actor = new Actor { CheckContainer = () => throw new InvalidOperationException("container not ready") };
        object[] args = { actor, null, new NullReferenceException("vanilla container unavailable") };
        Equal(null, FinalizeSprite(args), "a real fallback recovers the rendering exception");
        Equal(expected, args[1], "finalizer supplies a usable sprite");
    }

    private static void BrokenFallbackPreservesOriginalException()
    {
        SpriteTextureLoader.Load = _ => throw new InvalidOperationException("textures not ready");
        var original = new NullReferenceException("original render failure");
        object[] args = { new Actor { animation_container = new() }, null, original };
        Equal(original, FinalizeSprite(args), "a failed fallback does not replace the original exception");
    }

    private static void ValidIdleSpriteIsPreserved()
    {
        var expected = new Sprite();
        var actor = new Actor { animation_container = new() { idle = new() { frames = new[] { expected } } } };
        SpriteTextureLoader.Load = _ => throw new InvalidOperationException("valid containers do not reload textures");
        Sprite actual = null;
        Equal(false, AW_ActorMainSpritePatch.CalculateMainSprite_Prefix(actor, ref actual), "valid frame handled");
        Equal(expected, actual, "original idle frame retained");
    }

    private static void OtherSpeciesPassesThrough()
    {
        Sprite actual = null;
        Equal(true, AW_ActorMainSpritePatch.CalculateMainSprite_Prefix(new Actor { Xia = false }, ref actual), "other species retains vanilla rendering");
    }

    private static void NegativeHeadIndexIsRepaired()
    {
        Actor actor = HeadActor(-2);
        AW_AgePatch.CheckSpriteHead_Prefix(actor);
        Equal(1, actor.data.head, "negative restored head index gets deterministic valid selection");
        Equal(actor.animation_container.heads[1], actor.cached_sprite_head, "repaired head is rendered");
    }

    private static void OversizedHeadIndexIsRepaired()
    {
        Actor actor = HeadActor(100);
        AW_AgePatch.CheckSpriteHead_Prefix(actor);
        Equal(0, actor.data.head, "existing upper-bound repair is preserved");
    }

    private static void ValidHeadIndexIsPreserved()
    {
        Actor actor = HeadActor(1);
        AW_AgePatch.CheckSpriteHead_Prefix(actor);
        Equal(1, actor.data.head, "valid head selection remains unchanged");
    }

    private static void MissingEquipmentUsesOrdinaryHead()
    {
        Actor actor = HeadActor(1);
        actor.equipment = null;
        actor.is_profession_warrior = true;
        actor.Texture.has_advanced_textures = true;
        actor.animation_container.heads_male = actor.animation_container.heads;
        Equal(false, AW_AgePatch.CheckSpriteHead_Prefix(actor), "missing equipment does not dereference a removed slot");
        if (actor.cached_sprite_head == null) throw new InvalidOperationException("ordinary head must still be selected");
    }

    private static void MissingHelmetUsesOrdinaryHead()
    {
        Actor actor = HeadActor(1);
        actor.equipment.helmet = null;
        actor.is_profession_warrior = true;
        actor.Texture.has_advanced_textures = true;
        actor.animation_container.heads_male = actor.animation_container.heads;
        Equal(false, AW_AgePatch.CheckSpriteHead_Prefix(actor), "missing helmet slot does not break head selection");
        if (actor.cached_sprite_head == null) throw new InvalidOperationException("ordinary head must still be selected");
    }

    private static void UnassignedHeadUsesDeterministicSelection()
    {
        Actor actor = HeadActor(-1);
        AW_AgePatch.CheckSpriteHead_Prefix(actor);
        Equal(1, actor.data.head, "unassigned head keeps its original deterministic selection");
    }

    private static void BabyHeadRemainsHidden()
    {
        Actor actor = HeadActor(1);
        actor.Baby = true;
        actor.cached_sprite_head = new Sprite();
        Equal(false, AW_AgePatch.CheckSpriteHead_Prefix(actor), "b suppresses custom baby heads");
        Equal(null, actor.cached_sprite_head, "b clears previous custom head for babies");
    }

    private static void HanEmperorKeepsSpecialHead()
    {
        Actor actor = HeadActor(1);
        actor.is_profession_king = true;
        actor.HanEmperor = true;
        actor.Texture.has_advanced_textures = true;
        var expected = new Sprite();
        SpriteTextureLoader.LoadSingle = path => path == "xia/heads_special/head_king/head_han" ? expected : null;
        AW_AgePatch.CheckSpriteHead_Prefix(actor);
        Equal(expected, actor.cached_sprite_head, "b's Han emperor special head is retained");
    }

    private static void MissingHanHeadUsesOrdinaryKingHead()
    {
        Actor actor = HeadActor(1);
        actor.is_profession_king = true;
        actor.HanEmperor = true;
        actor.Texture.has_advanced_textures = true;
        var expected = new Sprite();
        SpriteTextureLoader.LoadSingle = path => path == "xia/heads_special/head_king/head_0" ? expected : null;
        AW_AgePatch.CheckSpriteHead_Prefix(actor);
        Equal(expected, actor.cached_sprite_head, "b's regular king fallback is retained");
    }

    private static Actor HeadActor(int head) => new()
    {
        data = new() { id = 7, head = head },
        animation_container = new() { heads = new[] { new Sprite(), new Sprite() } }
    };

    private static object FinalizeSprite(object[] args) => typeof(AW_ActorMainSpritePatch)
        .GetMethod("CalculateMainSprite_Finalizer", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException(message + $": expected {expected}, actual {actual}");
    }
}
