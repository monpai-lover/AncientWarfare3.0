using AncientWarfare3.patch.naming;
using UnityEngine.UI;

namespace UnityEngine
{
    internal static class Input
    {
        internal static Func<string> CompositionReader = () => string.Empty;
        public static string compositionString => CompositionReader();
    }

    internal static class Mathf
    {
        public static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }
    }
}

namespace UnityEngine.UI
{
    internal sealed class InputField
    {
        private int _caretPosition;
        private int _selectionAnchorPosition;
        private int _selectionFocusPosition;

        public bool isFocused { get; set; }
        public string text { get; set; } = string.Empty;
        public int caretPosition
        {
            get => _caretPosition;
            set
            {
                _caretPosition = value;
                _selectionAnchorPosition = value;
                _selectionFocusPosition = value;
            }
        }
        public int selectionAnchorPosition
        {
            get => _selectionAnchorPosition;
            set => _selectionAnchorPosition = value;
        }
        public int selectionFocusPosition
        {
            get => _selectionFocusPosition;
            set => _selectionFocusPosition = value;
        }
    }
}

internal static class Program
{
    private static int Main()
    {
        try
        {
            ActiveRewriteClampsCaretAndSelection();
            ActiveRewritePreservesInRangeSelection();
            ActiveCompositionDefersRewrite();
            CompositionReadFailureDefersRewrite();
            Console.WriteLine(
                "Actor manual-name InputField synchronizer tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void ActiveRewritePreservesInRangeSelection()
    {
        UnityEngine.Input.CompositionReader = () => string.Empty;
        var field = new InputField
        {
            isFocused = true,
            text = "Alexander",
            caretPosition = 6,
            selectionAnchorPosition = 2,
            selectionFocusPosition = 6
        };

        bool rewritten = ActorManualNameInputSynchronizer.TryRewrite(
            field, "Alexandria");

        True(rewritten, "idle active field accepts an in-range selection");
        Equal(6, field.caretPosition, "caret remains in range");
        Equal(2, field.selectionAnchorPosition,
            "caret assignment cannot collapse the saved selection anchor");
        Equal(6, field.selectionFocusPosition,
            "caret assignment cannot collapse the saved selection focus");
    }

    private static void ActiveRewriteClampsCaretAndSelection()
    {
        UnityEngine.Input.CompositionReader = () => string.Empty;
        var field = CreateActiveField();

        bool rewritten = ActorManualNameInputSynchronizer.TryRewrite(
            field, "Li");

        True(rewritten, "idle active field accepts a programmatic rewrite");
        Equal("Li", field.text, "programmatic rewrite updates field text");
        Equal(2, field.caretPosition, "caret is clamped to new text length");
        Equal(2, field.selectionAnchorPosition,
            "selection anchor is clamped to new text length");
        Equal(2, field.selectionFocusPosition,
            "selection focus is clamped to new text length");
    }

    private static void ActiveCompositionDefersRewrite()
    {
        UnityEngine.Input.CompositionReader = () => "li";
        var field = CreateActiveField();

        bool rewritten = ActorManualNameInputSynchronizer.TryRewrite(
            field, "Li");

        True(!rewritten, "active IME composition defers a rewrite");
        AssertUnchanged(field,
            "active IME composition preserves the current input state");
    }

    private static void CompositionReadFailureDefersRewrite()
    {
        UnityEngine.Input.CompositionReader = () =>
            throw new InvalidOperationException("composition unavailable");
        var field = CreateActiveField();

        bool rewritten = ActorManualNameInputSynchronizer.TryRewrite(
            field, "Li");

        True(!rewritten, "unreadable composition state fails closed");
        AssertUnchanged(field,
            "composition read failure preserves the current input state");
    }

    private static InputField CreateActiveField()
    {
        return new InputField
        {
            isFocused = true,
            text = "Alexander",
            caretPosition = 9,
            selectionAnchorPosition = 8,
            selectionFocusPosition = 7
        };
    }

    private static void AssertUnchanged(InputField field, string message)
    {
        Equal("Alexander", field.text, message + " text");
        Equal(9, field.caretPosition, message + " caret");
        Equal(8, field.selectionAnchorPosition, message + " anchor");
        Equal(7, field.selectionFocusPosition, message + " focus");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message +
                $" (expected {expected}, actual {actual})");
    }
}
