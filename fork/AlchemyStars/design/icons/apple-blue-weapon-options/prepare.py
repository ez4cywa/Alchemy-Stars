"""Normalize the generated partial sheet, then prepare named cutouts and QA boards.

Run with --sheet before oil-icon slice_icons.py, or without arguments after slicing.
The generated art is preserved; this script only crops, places and locks its palette.
"""
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
names = ['weapon-follow', 'weapon-processing-mode']
if '--sheet' in sys.argv:
    spec = json.loads((root.parent / 'apple-blue/style-spec.json').read_text(encoding='utf-8'))
    spec['name'] = 'alchemy-apple-blue-weapon-options'
    spec['icons'] = [dict(index=1, name=names[0], metaphor='one gripping hand with a curved follow arrow'),
                     dict(index=2, name=names[1], metaphor='one model part branching into left and right parts')]
    (root / 'style-spec.json').write_text(json.dumps(spec, ensure_ascii=False, indent=2), encoding='utf-8')
    source = Image.open(root / 'raw/generated.png').convert('RGB')
    sheet = Image.new('RGB', (1280, 1280), '#808080')
    # The generation drifted below the first row. Re-center the two isolated artworks
    # into the requested 4x4 grid before slicing, with no neighboring fragments.
    for i, box in enumerate([(0, 0, 360, 430), (360, 0, 800, 430)]):
        crop = source.crop(box)
        pixels = np.array(crop).astype(int)
        mask = np.abs(pixels - 128).max(axis=2) >= 30
        ys, xs = np.where(mask)
        crop = crop.crop((xs.min(), ys.min(), xs.max()+1, ys.max()+1))
        crop.thumbnail((224, 224), Image.Resampling.LANCZOS)
        sheet.paste(crop, (i*320+(320-crop.width)//2, (320-crop.height)//2))
    sheet.save(root / 'raw/sheet.png')
else:
    boards = {name: Image.new('RGB', (640, 320), color) for name, color in
              [('preview', '#e0e5ec'), ('qa-magenta', '#d946ef'), ('qa-dark', '#272729')]}
    for i, name in enumerate(names):
        src = root / f'png-512/{i+1:02}.png'
        im = Image.open(src).convert('RGBA')
        pixels = np.array(im)
        white = (pixels[:, :, :3] > 190).all(axis=2)
        pixels[:, :, :3] = np.where(white[:, :, None], [255, 255, 255], [0, 102, 204])
        im = Image.fromarray(pixels)
        for size in (512, 128, 64):
            dest = root / f'png-{size}'
            dest.mkdir(exist_ok=True)
            im.resize((size, size), Image.Resampling.LANCZOS).save(dest / f'{name}.png')
        for board in boards.values():
            icon = im.resize((180, 180), Image.Resampling.LANCZOS)
            board.paste(icon, (i*320+70, 20), icon)
            draw = ImageDraw.Draw(board)
            draw.text((i*320+60, 210), name, fill='#ffffff')
            # Actual header-size previews next to a larger high-DPI version.
            for x, size in [(80, 20), (130, 24), (185, 32)]:
                icon = im.resize((size, size), Image.Resampling.LANCZOS)
                board.paste(icon, (i*320+x, 260), icon)
        src.unlink()
    for name, board in boards.items():
        board.save(root / f'{name}.png')
