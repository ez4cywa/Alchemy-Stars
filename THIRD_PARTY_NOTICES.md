# Third-party notices

## ModelMergerGUI core

The CAST codec, merge, ammunition filling and preview analysis core under
`third_party/modelmerger` is from [ez4cywa/ModelMergerGUI](https://github.com/ez4cywa/ModelMergerGUI),
version 2.2.2, pinned at `ac0bfeb577651c34b3de4fdbafa4b1f615e5f84f`, under the MIT License.
The original license and provenance are retained in that directory. The new
JSONL adapter and Avalonia workspace integrate it into Alchemy Stars. No upstream
MiSans font or independent GUI updater is redistributed by this integration.

## Blender CAST plugin

The Blender add-on and Python CAST library under `third_party/cast/blender` are
from [dtzxporter/cast](https://github.com/dtzxporter/cast), pinned at
`a8ca18a0acf3b97b19332c53b54b47fcc3217755`, under the MIT License.
The license is retained at `third_party/cast/LICENSE`. Importer adaptations
bind an animation to the newly imported armature for a complete single-model scene
and increase joint display length to reduce orientation precision loss.
See `third_party/cast/blender/UPSTREAM.md` for provenance.

## Alchemist

Alchemy Stars is based on [Scobalula/Alchemist](https://github.com/Scobalula/Alchemist),
which is licensed under GPL-3.0. The full source and license are provided in
`fork/AlchemyStars`.

## RedFox and Cast.NET

The original Alchemist pipeline depends on Scobalula/RedFox and Cast.NET. Their
pinned source trees and license files are retained in the `fork/RedFox` Git
submodule.

## Maya CAST plugin

Alchemy Stars redistributes the Python CAST format library and Autodesk Maya
file translator from [dtzxporter/cast](https://github.com/dtzxporter/cast).
Those files are licensed under the MIT License; the original license text is
stored at `third_party/cast/LICENSE`.

Alchemy Stars physically combines viewhands, weapon, and attachment data into
one model node with one skeleton before writing CAST output. The plugin's
`importMerge` option is therefore not required for importing into a new scene;
it remains available for intentionally merging into an existing scene skeleton.
