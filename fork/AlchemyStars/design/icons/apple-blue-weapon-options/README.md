# Weapon option icons

Two oil-icon additions matching the existing AppleBlue family: `weapon-follow` uses a gripping hand and follow arrow; `weapon-processing-mode` uses one part branching into two. Existing dual-wield artwork remains unchanged.

Generated with built-in imagegen using the existing `apple-blue/preview.png` as style reference. `raw/generated.png` preserves the original; `prepare.py --sheet` corrects placement onto a 4x4 grey sheet with only two occupied cells. The oil-icon `slice_icons.py --mode floodfill --grid 4 --count 2 --thresh 30` removes the grey background. `prepare.py` locks the blue/white palette and produces 512/128/64 PNGs plus contrasting QA boards, including 20/24/32px checks.

Runtime assets use the 128px transparent PNGs scaled by the existing `product-icon` style. The frozen construction spec is in `style-spec.json`; generation instructions are in `prompt.txt`.
