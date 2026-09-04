# Third-Party Notices

## Cultiway-Reborn pathfinding

AncientWarfare3 includes an adapted implementation of the pathfinding design and code from:

- `Cultiway-Reborn/Source/Core/Pathfinding`
- `Cultiway-Reborn/Source/Patch/PatchAboutPathfinding.cs`
- `Cultiway-Reborn/Source/Utils/PriorityQueuePreview.cs`

The adapted implementation uses AncientWarfare3 namespaces, immutable worker snapshots, main-thread WorldBox adapters, and vanilla dock/boat transport. It does not include Cultiway cultivation, ECS, teleport-array, train, skill, building, or UI systems.

The source files listed above are licensed under the MIT License:

Copyright (c) 2025 Inmny

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Historical figure card audio

The historical figure card reveal audio was derived from the local
`cs2-case-simulator` reference repository at
`frontend/assets/audio`. Only the converted WAV files used by this feature are
included; no frontend source, database, or unrelated assets are bundled.

| Reference asset | Bundled asset |
| --- | --- |
| `generic_press_01.mp3` | `GameResources/sounds/historical_cards/aw3_card_button_press.wav` |
| `itemtile_plastic_rollover_15.mp3` | `GameResources/sounds/historical_cards/aw3_card_item_hover.wav` |
| `csgo_ui_crate_item_scroll.mp3` | `GameResources/sounds/historical_cards/aw3_card_scroll.wav` |
| `case_unlock_01.mp3` | `GameResources/sounds/historical_cards/aw3_card_unlock.wav` |
| `case_unlock_immediate_01.mp3` | `GameResources/sounds/historical_cards/aw3_card_unlock_immediate.wav` |
| `case_reveal_rare_01.mp3` | `GameResources/sounds/historical_cards/aw3_card_reveal_blue.wav` |
| `case_reveal_mythical_01.mp3` | `GameResources/sounds/historical_cards/aw3_card_reveal_purple.wav` |
| `case_reveal_legendary_01.mp3` | `GameResources/sounds/historical_cards/aw3_card_reveal_pink.wav` |
| `case_reveal_ancient_01.mp3` | `GameResources/sounds/historical_cards/aw3_card_reveal_red.wav` |
| `case_reveal_ancient_01.mp3` | `GameResources/sounds/historical_cards/aw3_card_reveal_gold.wav` |

The source repository is used only as a local audiovisual reference for this
feature. The card audio playback is optional and failures disable audio
without blocking card draws or deployments.

## Historical card stage and crate artwork

The historical card opening stage and crate artwork are adapted from the
local `cs2-case-simulator` reference repository. The stage source is
`frontend/public/backgrounds/de_ancient.webp`; the bundled PNG is resized and
slightly softened for the WorldBox UI. The crate source is
`frontend/assets/images/souvenir.webp`; the six bundled crate PNGs are
recolored variants used only to distinguish the historical periods.

## Cultiway-Reborn city-wall geometry

AncientWarfare3 includes an adapted implementation of the city-wall geometry
and placement design from:

- `Cultiway-Reborn/Source/Content/WallShapeHelper.cs`
- the city-wall portion of `Cultiway-Reborn/Source/Content/Plots.cs`

The adapted implementation uses AncientWarfare3 namespaces, a detached grid
geometry layer, and a WorldBox adapter that places original top-tile assets.
The source files listed above are licensed under the MIT License. The complete
license text is packaged in `THIRD_PARTY_NOTICES/Cultiway-Wall-MIT.txt`.
