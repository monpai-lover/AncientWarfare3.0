using System;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch.naming
{
    internal static class ActorManualNameInputSynchronizer
    {
        internal static bool CanRewrite(InputField pField)
        {
            if (pField == null) return false;
            if (pField.isFocused)
            {
                try
                {
                    if (!string.IsNullOrEmpty(Input.compositionString))
                        return false;
                }
                catch { return false; }
            }

            return true;
        }

        internal static bool TryRewrite(InputField pField, string pText)
        {
            if (!CanRewrite(pField)) return false;

            int caret = pField.caretPosition;
            int anchor = pField.selectionAnchorPosition;
            int focus = pField.selectionFocusPosition;

            pField.text = pText ?? string.Empty;
            int maximum = pField.text?.Length ?? 0;
            pField.caretPosition = Mathf.Clamp(
                caret, 0, maximum);
            pField.selectionAnchorPosition = Mathf.Clamp(
                anchor, 0, maximum);
            pField.selectionFocusPosition = Mathf.Clamp(
                focus, 0, maximum);
            return true;
        }
    }
}
