using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

True(RebellionDirectTerritoryTransferRules.ShouldTransfer(
        true, true, true, false, true, true, true),
    "an exact active rebellion transfers directly");
False(RebellionDirectTerritoryTransferRules.ShouldTransfer(
        true, true, true, false, true, true, false),
    "an ordinary war keeps frozen occupation");
False(RebellionDirectTerritoryTransferRules.ShouldTransfer(
        true, true, true, false, true, false, true),
    "same-side participants cannot authorize transfer");
False(RebellionDirectTerritoryTransferRules.ShouldTransfer(
        true, true, true, true, true, true, true),
    "an owner cannot capture its own city");
False(RebellionDirectTerritoryTransferRules.ShouldTransfer(
        false, true, true, false, true, true, true),
    "an invalid city fails closed");
True(RebellionDirectTerritoryTransferRules.BlocksOrdinarySettlement(
        true, true, true),
    "an active authoritative rebellion blocks ordinary peace");
False(RebellionDirectTerritoryTransferRules.BlocksOrdinarySettlement(
        true, true, false),
    "an ordinary active war remains negotiable");
Equal("rebellion_uses_direct_territory_transfer",
    RebellionDirectTerritoryTransferRules.SettlementBlockedReason,
    "the rejection reason is stable");

Console.WriteLine("AW3 rebellion direct-transfer rules passed.");

static void True(bool value, string name)
{
    if (!value)
        throw new InvalidOperationException(name + ": expected true");
}

static void False(bool value, string name)
{
    if (value)
        throw new InvalidOperationException(name + ": expected false");
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            name + ": expected " + expected + ", got " + actual);
}
