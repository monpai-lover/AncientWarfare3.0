# War Return And Runtime Display Design

## Goal

After a war ends, return only armies whose RTS missions were bound to that war to a friendly safe city, including cross-island transport, while preserving unrelated active-war missions and delaying temporary levy demobilization until arrival. Runtime declaration, war, and settlement text must use the live `War.name` when available and a localized fallback otherwise.

## Scope

This change covers ended-war army return intent, existing transport integration, temporary levy arrival demobilization, manual declaration regression coverage, real war-name presentation, and settlement summary presentation. It does not change war preparation candidate selection, recruitment algorithms, Mandate behavior, or lineage behavior.

## Army Return Architecture

`ArmyRtsControllerService.InvalidateWar` will snapshot only mission army ids bound to the ended war. Before invalidating each id, it will check whether another active-war mission has replaced the ended mission. Such a replacement is retained and does not enter return handling. An army whose ended-war mission is invalidated is handed to a bounded runtime return queue.

The return queue owns only movement intent. It resolves a live friendly safe city, moves the captain on land, and invokes the existing `ArmyRtsTransportService` for cross-island travel. It never teleports actors and never rewrites army composition. When the army reaches the safe city, standing and special armies only clear their return intent.

Temporary levies remain owned by the existing actor return path. That path continues to wait for a friendly safe city before calling `TemporaryMilitaryDemobilizationService.RestoreCivilian`; cross-island actors continue to use the existing RTS transport or taxi request. No standing or special army is demobilized by the new queue.

## Display Data Flow

The manual UI declaration path remains `DeclareWar` command dispatch to `DiplomaticWarDeclarationService.TryIssue`, followed by `WarNoticeService.EnsureCurrentNotice` and the existing diplomatic notice record. It receives regression coverage only.

Live presentation uses `pWar.name` as the authoritative war name. If it is unavailable, a helper localizes the war asset's `localized_war_name` key and only then uses a localized generic fallback. Chronicle start/end records receive that resolved name instead of a war-type id.

Automatic settlement truce rows persist the resolved live war name in their currently unused `DETAIL_ID`. Settlement rendering reads that stored name and actual settlement terms for the war id. Newly recorded war-ended conversation events carry the resolved name and derive the outcome from `WarWinner`/the real winning kingdom, rather than exposing format keys or placeholders. The missing `war_tributary` name-template whitelist entry is restored so generated names do not degrade to conquest names.

## Failure Handling

Dead or missing armies, captains, kingdoms, and cities are removed from the bounded return queue. A lost or captured target city triggers safe-city re-resolution. Active voyages remain in the existing transport service. Missing live war names use localized keys through the localization API and never display a localization key directly.

## Testing

Rules tests will first fail for: exact ended-war mission ownership and parallel-war preservation; bounded return lifecycle and transport wiring; arrival-only levy demobilization and non-demobilization of standing/special armies; manual declaration notice wiring; real-name chronicle/conversation/settlement flow; localized fallback; and the tributary template. After implementation, run the focused rules slice, the complete `AncientWarfare3.Rules.Tests` executable, and `git diff --check`.
