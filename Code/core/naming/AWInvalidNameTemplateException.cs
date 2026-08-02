using System;

namespace AncientWarfare3.core.naming
{
    public sealed class AWInvalidNameTemplateException : Exception
    {
        public AWInvalidNameTemplateException(string pMessage)
            : base(pMessage)
        {
        }

        public AWInvalidNameTemplateException(char pCharacter, int pIndex,
            string pTemplate, string pContext)
            : base($"Invalid character '{pCharacter}' at {pIndex} in " +
                   $"\"{pTemplate}\" ({pContext}).")
        {
        }
    }
}
