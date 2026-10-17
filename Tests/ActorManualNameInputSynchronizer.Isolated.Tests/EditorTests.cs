using System.Reflection;
using AncientWarfare3.core.naming;
using AncientWarfare3.patch.naming;
using UnityEngine;
using UnityEngine.UI;

internal static class EditorTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal static void Run()
    {
        EnterEditingDefersUntilCompositionEnds();
        UnfocusedDeferredEditDoesNotStealFocus();
        CollapsePreservesBothFieldsDuringComposition();
        SwitchingActorsCancelsPendingEdits();
        DisablingCancelsPendingEdits();
        ReenabledGivenFieldRestoresOnlyItsOwnValue();
    }

    private static void EnterEditingDefersUntilCompositionEnds()
    {
        var editor = new Fixture();
        editor.First.inputField.isFocused = true;
        editor.First.inputField.caretPosition = 17;
        Input.CompositionReader = () => "li";
        editor.Call("EnterEditing");
        editor.AssertState(ActorManualNameEditorState.Display);
        Equal("Alexander Macedon", editor.First.inputField.text, "composition preserves display text");
        Equal(false, editor.Second.gameObject.activeSelf, "composition does not split the editor");
        editor.Tick();
        editor.AssertState(ActorManualNameEditorState.Display);

        Input.CompositionReader = () => string.Empty;
        editor.Tick();
        editor.AssertState(ActorManualNameEditorState.Editing);
        Equal("Alexander", editor.First.inputField.text, "retry loads given name");
        Equal("Macedon", editor.Second.inputField.text, "retry loads family name");
        Equal(9, editor.First.inputField.caretPosition, "production patch clamps caret after splitting");
        Equal(true, editor.Second.gameObject.activeSelf, "retry expands editor");

        editor.First.inputField.text = "Alex";
        editor.First.inputField.isFocused = false;
        editor.First.inputField.onEndEdit.Invoke("Alex");
        editor.Tick();
        editor.AssertState(ActorManualNameEditorState.Display);
        Equal("Alex Macedon", editor.Window.actor.getName(), "focus loss commits both fields");
        Equal("Alex Macedon", editor.First.inputField.text, "commit restores full name");
    }

    private static void UnfocusedDeferredEditDoesNotStealFocus()
    {
        var editor = new Fixture();
        editor.First.inputField.isFocused = true;
        Input.CompositionReader = () => "li";
        editor.Call("EnterEditing");
        editor.First.inputField.isFocused = false;
        Input.CompositionReader = () => string.Empty;
        editor.Tick();
        editor.AssertState(ActorManualNameEditorState.Display);
        Equal(false, editor.First.inputField.isFocused, "retry does not reactivate an abandoned editor");
    }

    private static void CollapsePreservesBothFieldsDuringComposition()
    {
        var editor = new Fixture();
        Input.CompositionReader = () => string.Empty;
        editor.Call("EnterEditing");
        editor.First.inputField.isFocused = false;
        editor.Second.inputField.isFocused = true;
        editor.Second.inputField.text = "draft family";
        Input.CompositionReader = () => "li";
        editor.Call("ShowDisplay");
        editor.AssertState(ActorManualNameEditorState.Editing);
        Equal("Alexander", editor.First.inputField.text, "second-field composition prevents first-field overwrite");
        Equal("draft family", editor.Second.inputField.text, "second-field composition preserves draft");
        Equal(true, editor.Second.gameObject.activeSelf, "second-field composition prevents collapse");

        Input.CompositionReader = () => string.Empty;
        editor.Tick();
        editor.AssertState(ActorManualNameEditorState.Display);
        Equal(false, editor.Second.gameObject.activeSelf, "collapse retries after composition ends");
    }

    private static void SwitchingActorsCancelsPendingEdits()
    {
        var editor = new Fixture();
        editor.First.inputField.isFocused = true;
        Input.CompositionReader = () => "li";
        editor.Call("EnterEditing");
        editor.Window.actor = new Actor { data = new ActorData { id = 2 }, Given = "New", Family = "Actor" };
        Input.CompositionReader = () => string.Empty;
        editor.Load();
        editor.Tick();
        editor.AssertState(ActorManualNameEditorState.Display);
        Equal("New Actor", editor.First.inputField.text, "actor switch cancels stale edit retry");
    }

    private static void DisablingCancelsPendingEdits()
    {
        var editor = new Fixture();
        editor.First.inputField.isFocused = true;
        Input.CompositionReader = () => "li";
        editor.Call("EnterEditing");
        typeof(ActorManualNameEditTrigger).GetMethod("OnDisable", Private).Invoke(editor.Trigger, null);
        Input.CompositionReader = () => string.Empty;
        editor.Tick();
        editor.AssertState(ActorManualNameEditorState.Display);
    }

    private static void ReenabledGivenFieldRestoresOnlyItsOwnValue()
    {
        var editor = new Fixture();
        editor.Window.actor.Mode = ActorManualNameMode.Xia;
        editor.Window.actor.Family = "李";
        editor.Window.actor.Given = "白";
        // NameInput is cloned from the full-name field. Its copied LastValue
        // is used by vanilla checkInput when OnEnable reattaches validation.
        editor.Second.LastValue = "李白";
        editor.Load();
        editor.Call("EnterEditing");
        editor.First.inputField.isFocused = false;
        editor.Second.inputField.isFocused = false;
        editor.Second.inputField.text = string.Empty;
        editor.Second.inputField.onEndEdit.Invoke(string.Empty);
        Equal("白", editor.Second.inputField.text,
            "vanilla required-field validation restores the given name, not the cloned full name");
        editor.Tick();
        Equal("李白", editor.Window.actor.getName(),
            "validation cannot duplicate the family component on commit");
    }

    private sealed class Fixture
    {
        internal readonly NameInput First = CreateInput("First");
        internal readonly NameInput Second = CreateInput("AW_ActorManualNameSecondInput");
        internal readonly UnitWindow Window;
        internal ActorManualNameEditTrigger Trigger => First.GetComponent<ActorManualNameEditTrigger>();

        internal Fixture()
        {
            var parent = new GameObject("NameInputs");
            First.transform.SetParent(parent.transform, false);
            Second.transform.SetParent(parent.transform, false);
            Window = new UnitWindow { actor = new Actor(), name_input = First };
            Input.CompositionReader = () => string.Empty;
            Load();
        }

        private object State
        {
            get
            {
                object states = typeof(AW_ActorManualNamePatch).GetField("States", Private).GetValue(null);
                return states.GetType().GetMethod("GetOrCreateValue").Invoke(states, new object[] { Window });
            }
        }

        internal void Load() => typeof(AW_ActorManualNamePatch).GetMethod("LoadNameInput_Postfix", Private)
            .Invoke(null, new object[] { Window });
        internal void Call(string method) => typeof(AW_ActorManualNamePatch).GetMethod(method, Private)
            .Invoke(null, new object[] { Window, First, State });
        internal void Tick() => typeof(ActorManualNameEditTrigger).GetMethod("LateUpdate", Private).Invoke(Trigger, null);
        internal void AssertState(ActorManualNameEditorState expected) => Equal(expected,
            (ActorManualNameEditorState)State.GetType().GetField("State", Private).GetValue(State), "editor state");
    }

    private static NameInput CreateInput(string name)
    {
        var gameObject = new GameObject(name, typeof(RectTransform), typeof(NameInput), typeof(InputField), typeof(Text));
        NameInput input = gameObject.GetComponent<NameInput>();
        input.inputField = gameObject.GetComponent<InputField>();
        input.textField = gameObject.GetComponent<Text>();
        input.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 20);
        return input;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}
