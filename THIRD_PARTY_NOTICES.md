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

## Cultiway-Reborn city-wall geometry

AncientWarfare3 includes an adapted implementation of the city-wall geometry
and placement design from:

- `Cultiway-Reborn/Source/Content/WallShapeHelper.cs`
- the city-wall portion of `Cultiway-Reborn/Source/Content/Plots.cs`

The adapted implementation uses AncientWarfare3 namespaces, a detached grid
geometry layer, and a WorldBox adapter that places original top-tile assets.
The source files listed above are licensed under the MIT License. The complete
license text is packaged in `THIRD_PARTY_NOTICES/Cultiway-Wall-MIT.txt`.
