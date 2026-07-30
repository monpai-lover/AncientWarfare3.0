# Coalition Occupation Recipient Choice Design

Date: 2026-07-30

## Goal

When a winning-side ally controls an enemy city, the war leader must be able
to choose whether the peace settlement gives that city to the occupying ally
or to the war leader. White peace and unrelated treaty terms remain available.

## Candidate Generation

Frozen occupation records remain the authority for the city's wartime home
realm and controlling side. For each enemy city controlled by the winning
side:

- Always generate a cession candidate for the actual controlling kingdom.
- When the controller is not the negotiating winning-side war leader, also
  generate a cession candidate for that war leader.
- Both candidates use the same city value and war-score cost.
- Do not generate the war-leader alternative for ordinary cores or claims
  without a winning-side frozen occupation.
- Do not allow arbitrary allies or third-party kingdoms as recipients.

The two candidates remain distinct because their recipient kingdom IDs differ.
The displayed detail must include both the city name and recipient realm so the
choice is unambiguous.

## Selection Rules

Only one cession term may target a given city. Selecting either recipient in
the negotiation window automatically deselects the other recipient for that
city. Server-side materialization continues to reject a draft containing two
cession terms for the same city.

The war leader may take the city without the occupying ally's consent. This
choice does not create an opinion penalty.

## Validation And Execution

A cession recipient is valid only when it is either:

- the kingdom recorded as the frozen occupation controller; or
- the war leader on that controller's side.

The frozen record must identify the term's source kingdom as the city's wartime
home kingdom. The controller must still belong to the recipient war leader's
side. A city owned by an unrelated third party remains unavailable.

Normal frozen occupation leaves the live city owner as the source kingdom. For
legacy or projected states, execution may also proceed when the live owner is
the recorded controller and the selected recipient is its war leader. The
settlement then performs the permanent transfer to the selected recipient.

## AI Behavior

AI-generated peace bundles prefer the actual occupation controller when two
equal-cost recipient candidates exist for the same city. The war-leader option
is primarily a player choice and remains available to AI only when no preferred
controller candidate can be selected.

## Tests

Regression coverage must prove that:

- an allied frozen occupation yields both controller and war-leader candidates;
- a war leader's own occupation yields only one candidate;
- both candidates have the same city and cost but different recipients;
- selecting one recipient removes the other selection for that city;
- duplicate-city terms are rejected server-side;
- a third-party or opposing-side recipient is rejected;
- AI chooses the actual controller before the war leader at equal cost;
- the compact negotiation list shows the recipient realm in each city term.

## Scope

This change does not add ally consent, opinion penalties, arbitrary recipient
selection, a separate recipient picker window, or changes to city valuation.
