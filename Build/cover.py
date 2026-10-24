# Draws About/Preview.png in the look of the RimTalk family's covers: their dark brown ground,
# tan title and pixel speech bubble, with RimTalk's own pixel colonist cut from its cover (as the
# other add-ons do), and a pair of stage curtains for Stagecraft. Run: py -3 Build/cover.py
import os
from PIL import Image, ImageDraw, ImageFont

here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RIMTALK_COVER = r'E:\SteamLibrary\steamapps\workshop\content\294100\3551203752\About\Preview.png'
FONT = r'C:\Windows\Fonts\bahnschrift.ttf'

W, H = 1280, 720
BG = (43, 33, 21)
TAN = (230, 195, 143)
INK = (22, 16, 12)
PAPER = (244, 226, 184)
PX = 8   # one "pixel" of the hand-drawn parts

img = Image.new('RGB', (W, H), BG)
d = ImageDraw.Draw(img)

def font(size, weight='Bold'):
    f = ImageFont.truetype(FONT, size)
    try:
        f.set_variation_by_name(weight)
    except Exception:
        pass
    return f

# Curtains: deep red cloth in folds, a tie-back, and the scalloped hem of a valance above.
CLOTH = [(92, 24, 28), (122, 34, 36), (150, 46, 44), (122, 34, 36)]
GOLD = (196, 152, 74)

TIE = 432   # height of the tie-back

def curtain(width, right):
    cols = width // PX
    narrow = cols * 0.42
    for r in range(H // PX):
        y = r * PX
        # Full width at the top, drawn in to the tie-back, falling open again to the floor.
        if y <= TIE:
            t = (y / TIE) ** 1.6
        else:
            t = 1 - ((y - TIE) / (H - TIE)) ** 0.7
        cur = max(1, int(round(cols - (cols - narrow) * t)))
        for k in range(cur):
            # The folds bunch up where the cloth is drawn in.
            shade = CLOTH[(k * cols // cur // 2) % len(CLOTH)]
            x = W - (k + 1) * PX if right else k * PX
            d.rectangle([x, y, x + PX - 1, y + PX - 1], fill=shade)
    tw = int(narrow + 2) * PX
    tx = W - tw if right else 0
    d.rectangle([tx, TIE - PX, tx + tw - 1, TIE + PX - 1], fill=GOLD)
    d.rectangle([tx, TIE + PX, tx + tw - 1, TIE + 2 * PX - 1], fill=(150, 112, 52))

curtain(152, False)
curtain(152, True)
# valance with a scalloped hem
d.rectangle([0, 0, W, 4 * PX - 1], fill=CLOTH[0])
for i in range(0, W, 10 * PX):
    d.rectangle([i, 4 * PX, i + 8 * PX - 1, 5 * PX - 1], fill=CLOTH[0])
    d.rectangle([i + PX, 5 * PX, i + 7 * PX - 1, 6 * PX - 1], fill=CLOTH[0])
d.rectangle([0, 4 * PX - PX, W, 4 * PX - 1], fill=GOLD)

# Title
def centered(text, f, y, fill):
    box = d.textbbox((0, 0), text, font=f)
    d.text(((W - (box[2] - box[0])) // 2 - box[0], y), text, font=f, fill=fill)
centered('RimTalk', font(150, 'Bold'), 62, TAN)
centered('Stagecraft', font(78, 'SemiBold'), 236, TAN)

# RimTalk's pixel colonist, cut from its own cover and doubled without smoothing.
src = Image.open(RIMTALK_COVER).convert('RGB')
pawn = src.crop((122, 170, 241, 331)).resize((238, 322), Image.NEAREST)
px, py = 236, H - 322
img.paste(pawn, (px, py))

# Speech bubble, pixel style: paper fill, ink rim with stepped corners, a tail toward the colonist.
bx0, by0, bx1, by1 = 520, 380, 1090, 600
d.rectangle([bx0 + PX, by0, bx1 - PX, by1], fill=INK)
d.rectangle([bx0, by0 + PX, bx1, by1 - PX], fill=INK)
d.rectangle([bx0 + PX, by0 + PX, bx1 - PX, by1 - PX], fill=PAPER)
d.rectangle([bx0 + 2 * PX, by0 + PX, bx1 - 2 * PX, by0 + 2 * PX - 1], fill=PAPER)
# Tail toward the colonist's mouth: a stepped wedge, ink rim around paper.
TY = 548
for i in range(6):
    y = TY - 3 * PX + i * PX
    reach = (6 - abs(i - 2) * 2) * PX if i <= 2 else (6 - (i - 2) * 2) * PX
    reach = max(reach, PX)
    d.rectangle([bx0 - reach - PX, y, bx0 + PX - 1, y + PX - 1], fill=INK)
for i in range(1, 5):
    y = TY - 3 * PX + i * PX
    reach = (6 - abs(i - 2) * 2) * PX if i <= 2 else (6 - (i - 2) * 2) * PX
    if reach > PX:
        d.rectangle([bx0 - reach + PX, y, bx0 + PX - 1, y + PX - 1], fill=PAPER)
f = font(52, 'SemiBold')
lines = ['//Amy and Bob argue', 'about dinner//']
ly = by0 + 52
for line in lines:
    box = d.textbbox((0, 0), line, font=f)
    d.text(((bx0 + bx1) // 2 - (box[2] - box[0]) // 2 - box[0], ly), line, font=f, fill=INK)
    ly += 70

out = os.path.join(here, 'About', 'Preview.png')
img.save(out, optimize=True)
print('saved', out, os.path.getsize(out), 'bytes')
