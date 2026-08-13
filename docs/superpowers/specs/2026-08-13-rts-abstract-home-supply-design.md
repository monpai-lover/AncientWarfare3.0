# RTS Abstract Home Supply Design

## Goal

Prevent soldiers on an active RTS mission from abandoning their orders or
starving while they are far from home. Supplies remain finite: every meal is
deducted from the soldier's own home city.

## Scope

The rule applies only to a live actor that is currently owned by a valid RTS
army mission. Royal guards, ordinary citizens, inactive armies, and actors
without a valid home city keep the vanilla hunger system unchanged.

## Design

When the vanilla hunger system is about to assign the `try_to_eat_city_food`
task to an eligible RTS actor, the logistics service attempts a remote meal:

1. Resolve the actor's home city from the army's existing recruitment/return
   ownership data. The city must still exist and belong to the actor's kingdom.
2. Select an edible resource from that city's real storage using the vanilla
   `getFoodItem` API, honoring the actor's diet and preferred food.
3. Deduct exactly one item with the vanilla city food-consumption API, then
   invoke the vanilla actor food-consumption API so nutrition, happiness,
   favorite-food effects, health restoration, and consumption statistics stay
   consistent.
4. Suppress only that hunger-driven task assignment after a successful meal.

If no valid home city or suitable stored food exists, the hook makes no
intervention. Vanilla starvation and its normal task behavior then proceed.

## Performance And Safety

The service performs no kingdom-wide city scan, creates no food, and does not
cache mutable food quantities. It resolves one home city and asks its existing
storage API for one item only at the vanilla hunger interval. All failures are
fail-open to vanilla behavior.

## Observability

When RTS diagnostics are enabled, successful remote meals and supply failures
will identify the actor, army, and home city. Logs are rate-limited so a
starving army cannot flood `Player.log`.

## Tests

Pure rules tests will cover eligibility and all supply outcomes. A source guard
will ensure the Harmony hook only intercepts the city-food task and preserves
the vanilla fallback when supply cannot be consumed.
