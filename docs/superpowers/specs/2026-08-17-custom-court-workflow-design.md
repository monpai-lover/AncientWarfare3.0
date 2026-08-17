# Custom Court Workflow Design

Status: design approved in conversation; implementation pending written-spec review.

## Goal

Allow a player to design reusable court templates, arrange offices as a card-based workflow, export/import the design as JSON, and apply a template to individual kingdoms without breaking the existing court, appointment, school, succession, or multiplayer systems.

The feature is a template layer over the existing court implementation. Built-in Xia and Western court profiles remain valid and continue to be the fallback for kingdoms that do not use a custom template.

## Decisions

- Templates are reusable and shareable.
- Each kingdom has an instance of the selected template and may override selected fields.
- Effects are composed from whitelisted preset modules with bounded numeric parameters.
- No C# code, formulas, scripts, or arbitrary callbacks are accepted from JSON.
- Players create, import, export, and apply templates.
- AI may choose among installed templates when allowed by existing court rules, but it never edits a template.
- The editor is a card-based workflow canvas rather than a table editor.
- Edges have two explicit meanings: management hierarchy and appointment/promotion prerequisites.

## Existing Integration Points

The implementation should reuse these existing boundaries:

- `CourtOfficeDefinition` for office metadata and institution availability.
- `CourtProfileRegistry` and `ICourtProfile` for built-in profile lookup.
- `CourtService` for court snapshots, office lookup, appointments, candidate eligibility, and court projections.
- `CourtAppointmentWindow` for candidate selection and appointment commands.
- `CourtWindow` for the court overview and hierarchy presentation.
- `AW3CourtCommandHandler` and `AW3AuthoritativeCommandRouter` for host-authoritative mutations.
- `CourtStateCodec` or a dedicated codec for compact persisted state.

The custom layer must be resolved before callers consume office definitions. Existing callers should not directly branch on whether an office is built-in or custom.

## Domain Model

### Court template

```text
CourtTemplate
  schemaVersion: int
  templateId: string
  revision: int
  displayName: localized text map
  description: localized text map
  baseProfile: built-in profile id
  allowedInstitutions: string[]
  nodes: CourtTemplateOffice[]
  edges: CourtTemplateEdge[]
  metadata: source, author, createdAt, updatedAt
```

### Office node

```text
CourtTemplateOffice
  id: stable template-local id
  name: localized text map
  layer: central | military | local | feudatory
  grade: bounded integer
  order: bounded integer
  slotCount: bounded integer
  preferredSchool: existing school id or none
  militaryCapable: bool
  requirements: whitelist of candidate facts and thresholds
  effects: whitelist effect modules with bounded values
  enabled: bool
  layout: canvas x/y position and optional lane
```

### Edge

```text
CourtTemplateEdge
  id: stable edge id
  fromOfficeId: string
  toOfficeId: string
  kind: management | appointment_prerequisite | promotion_prerequisite
  rankDelta: optional bounded integer
  condition: whitelist condition object
```

Management edges determine the hierarchy shown by the court pyramid. Prerequisite edges are evaluated by appointment and promotion rules. The graph must be acyclic for all edge kinds. Invalid, duplicate, dangling, or cyclic edges reject import.

### Kingdom instance

```text
CourtTemplateInstance
  templateId: string
  templateRevision: int
  instanceRevision: int
  resolvedSnapshot: serialized resolved office/edge definitions
  overrides: office-level name, enabled, slot, requirement, and effect overrides
  migrationState: pending office migrations and preserved legacy offices
```

The resolved snapshot is important: changing or deleting a local template must not silently change an active kingdom. A kingdom may explicitly upgrade to a newer template revision after reviewing a diff.

## JSON Contract

Example:

```json
{
  "schemaVersion": 1,
  "templateId": "xia_three_departments_custom",
  "revision": 1,
  "displayName": {
    "zh-Hans": "自定义三省六部",
    "zh-Hant": "自訂三省六部",
    "en": "Custom Three Departments"
  },
  "baseProfile": "xia",
  "nodes": [
    {
      "id": "grand_steward",
      "name": { "zh-Hans": "大司农", "en": "Grand Minister of Agriculture" },
      "layer": "central",
      "grade": 1,
      "order": 10,
      "slotCount": 1,
      "preferredSchool": "nong",
      "militaryCapable": false,
      "requirements": {
        "adult": true,
        "minimumStewardship": 8,
        "allowedSchools": ["nong", "ru"]
      },
      "effects": [
        { "id": "tax_income", "mode": "add_percent", "value": 10 },
        { "id": "food_production", "mode": "add_percent", "value": 15 }
      ],
      "enabled": true,
      "layout": { "x": 420, "y": 160, "lane": "central" }
    }
  ],
  "edges": []
}
```

The exporter writes normalized property ordering and UTF-8 without a BOM. Import accepts only known schema versions and can offer a migration step for older supported versions.

## Effect and Requirement Safety

Effects and requirements are registries, not executable data. Initial effect modules include:

- tax income
- food production
- city order and unrest
- technology output and spread
- military slots, training, and morale
- diplomacy
- school influence
- court efficiency and political points
- vassal loyalty

Every module defines its valid mode, minimum, maximum, stacking rule, and whether it applies to the kingdom, city, office holder, or court. Unknown modules, unsupported modes, NaN/infinite values, and out-of-range values are rejected or clamped with a visible warning before installation.

Requirements use the same whitelist approach for adult status, domestic status, school affiliation, statistics, official rank, local grade, traits, military identity, city scope, and prerequisite offices.

## Resolver Architecture

Add a single resolution boundary, conceptually:

```text
CourtDefinitionResolver.Resolve(kingdom, officeId)
CourtDefinitionResolver.ResolveGraph(kingdom)
```

Resolution order:

1. Kingdom instance override or resolved snapshot.
2. Kingdom's active custom template.
3. Existing built-in `CourtProfileRegistry` profile.
4. Empty/vacant definition.

`CourtService`, `CourtWindow`, `CourtAppointmentWindow`, candidate queries, promotion logic, school affinity, AI court logic, and office-name rendering consume this resolver. Built-in profile code remains unchanged as the fallback path.

Management edges feed the court pyramid read model. Prerequisite edges feed candidate and promotion validation. A missing prerequisite office produces an ineligible result, not an exception.

## Workflow Editor

The editor is a wide window with four areas:

### Template library

- new, copy, import, export, rename, delete
- schema and revision display
- base profile and institution compatibility

### Card palette

Cards are grouped by central, military, local, feudatory, and special office types. Dragging a palette card onto the canvas creates a new stable office ID. Copying an existing card creates a new ID and preserves only safe editable properties.

### Workflow canvas

- draggable office cards
- pan and zoom
- lane snapping
- connection handles for management and prerequisite edges
- visible cycle and dangling-edge diagnostics
- card badges for grade, slot count, effect count, and incumbent count
- optional minimap for large courts

### Inspector

- localized names and description
- layer, grade, order, and slot count
- candidate requirements
- effect module picker and bounded numeric controls
- edge type and prerequisite condition editor
- delete/duplicate/reset controls

The editor must not expose internal IDs as free-form text. IDs are generated, preserved on rename, and shown only as a diagnostic detail.

## Applying Templates

The kingdom application view shows a diff between the current resolved court and the selected template:

- added offices
- changed names, slots, grades, requirements, or effects
- removed offices
- changed management edges
- changed prerequisite edges

Apply modes:

1. Apply template and preserve current officers by office ID where compatible.
2. Apply template and create vacancies for incompatible offices.
3. Apply template with explicit migration choices for removed offices.

Existing officers are never silently deleted. A removed office becomes a preserved legacy office until the player migrates or dismisses its incumbent. The operation is atomic and can be rolled back before commit.

## Persistence and File Handling

Templates live in the WorldBox user-data area under an AncientWarfare3 court-template directory, not in the installed Mods directory. The exact path should use the existing mod data-path helper if one is available; otherwise use the WorldBox persistent data path.

Import writes to a temporary file, validates and normalizes the complete document, then atomically replaces the target. A failed import leaves the previous template untouched. Export never includes kingdom IDs, actor IDs, or save-specific data.

Template deletion removes only the local reusable template. Active kingdom instances retain their resolved snapshots.

## Multiplayer Authority

Template editing and local import/export are client-local operations. Applying a template, changing a kingdom instance, migrating officers, or deleting an active instance are authoritative court commands.

The host validates the normalized template payload, schema, revision, effect registry, and graph hash. Clients submit a canonical payload hash and receive the authoritative result. If a client does not have the referenced template, the UI shows a missing-template state and does not substitute a different file.

All mutation paths must respect the existing replica/apply scopes and use the current `AW3CourtCommandHandler` routing pattern.

## Migration and Compatibility

- Existing worlds with no custom fields continue using built-in court profiles.
- Existing actors with `COURT_OFFICE_ID` remain valid when the office ID still resolves.
- When an office disappears from a new template, preserve the incumbent as a legacy office record.
- When an office changes layer or grade, show a migration warning and do not auto-promote or auto-dismiss.
- Template schema migrations are explicit, versioned, and tested independently from save migration.
- Removing a template file cannot invalidate an active kingdom instance.

## Verification Plan

Pure rules tests:

- schema and range validation
- stable ID generation and collision handling
- graph acyclicity and dangling-edge rejection
- management/prerequisite edge semantics
- effect stacking and bounds
- diff and migration classification

Integration tests:

- resolver precedence between instance, template, and built-in profile
- appointment candidate filtering through custom requirements
- court pyramid construction from management edges
- promotion validation through prerequisite edges
- apply/rollback and legacy incumbent preservation
- old-save fallback behavior
- host/client template hash agreement and authority rejection

Manual verification:

- create a template from an existing office
- drag cards into a three-level court
- connect management and prerequisite edges
- export, delete, re-import, and compare normalized JSON
- apply to one kingdom while another remains on the built-in profile
- reload the save and confirm officers, names, effects, and layout persist

## Delivery Phases

1. Add pure template models, JSON codec, validator, effect/requirement registries, and tests.
2. Add local template storage, import/export, normalization, and migration diagnostics.
3. Add the resolver and route existing court reads through it while keeping built-in fallback behavior.
4. Add the card workflow editor and template library UI.
5. Add kingdom application, diff, migration, rollback, and authoritative multiplayer commands.
6. Add effects, prerequisite evaluation, AI template selection, localization, performance guards, and release verification.

The first implementation should ship with a small effect registry and no arbitrary scripting. More effect modules can be added later without changing the JSON contract.
