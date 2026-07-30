using System;

namespace AncientWarfare3.api.multiplayer
{
    public interface IAW3CommandDispatcher
    {
        event Action Changed;

        AW3CommandResult Dispatch(AW3CommandRequest request);
    }
}
