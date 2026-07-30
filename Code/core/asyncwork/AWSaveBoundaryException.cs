using System;

namespace AncientWarfare3.core.asyncwork
{
    internal static class AWSaveBoundaryException
    {
        public static InvalidOperationException CreateBlocked(
            string pError, Exception pCause)
        {
            return new InvalidOperationException(
                "World save blocked: " + pError, pCause);
        }
    }
}
