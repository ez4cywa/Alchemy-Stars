"""Original Ubuntu/Suru-inspired vector icon family, rendered for Alchemy Stars.
Requires Pillow. Run from any directory; writes reproducible assets under output/ubuntu-yaru-assets.
"""
from pathlib import Path
import json, math, zipfile
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output' / 'ubuntu-yaru-assets'
OUT.mkdir(parents=True, exist_ok=True)
(OUT / 'icons').mkdir(exist_ok=True)
(OUT / 'Source').mkdir(exist_ok=True)
S = 4
WHITE = '#FFFFFF'
COLORS = {'orange':'#CE4C16', 'purple':'#735080', 'blue':'#287F9D', 'green':'#3F8061', 'red':'#BE4044', 'gray':'#596472'}

class Vector:
    def __init__(self, color):
        self.img=Image.new('RGBA',(64*S,64*S))
        self.d=ImageDraw.Draw(self.img)
        self.svg=[]
        self.color=color
        self.rect(7,9,57,58,12,'#252525')
        self.rect(7,7,57,55,12,color)
        self.line([(19,8.5),(45,8.5)],'#FFFFFF',0.6)
    def rect(self,x1,y1,x2,y2,r=0,fill=None,stroke=None,w=2.5):
        self.d.rounded_rectangle(tuple(int(v*S) for v in (x1,y1,x2,y2)),radius=int(r*S),fill=fill,outline=stroke,width=max(1,int(w*S)))
        self.svg.append(f'<rect x="{x1}" y="{y1}" width="{x2-x1}" height="{y2-y1}" rx="{r}" fill="{fill or "none"}" stroke="{stroke or "none"}" stroke-width="{w}"/>')
    def line(self,points,color=WHITE,w=3):
        self.d.line([(int(x*S),int(y*S)) for x,y in points],fill=color,width=max(1,int(w*S)),joint='curve')
        for x,y in (points[0],points[-1]):
            self.d.ellipse(((x-w/2)*S,(y-w/2)*S,(x+w/2)*S,(y+w/2)*S),fill=color)
        self.svg.append(f'<polyline points="{" ".join(f"{x},{y}" for x,y in points)}" fill="none" stroke="{color}" stroke-width="{w}" stroke-linecap="round" stroke-linejoin="round"/>')
    def poly(self,points,fill=WHITE):
        self.d.polygon([(int(x*S),int(y*S)) for x,y in points],fill=fill)
        self.svg.append(f'<polygon points="{" ".join(f"{x},{y}" for x,y in points)}" fill="{fill}"/>')
    def circle(self,x,y,r,fill=None,stroke=WHITE,w=2.5):
        self.d.ellipse(((x-r)*S,(y-r)*S,(x+r)*S,(y+r)*S),fill=fill,outline=stroke,width=int(w*S))
        self.svg.append(f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill or "none"}" stroke="{stroke or "none"}" stroke-width="{w}"/>')
    def arrow(self, direction='up', x=32,y=32):
        p=[(0,10),(0,-10),(-7,-3),(0,-10),(7,-3)]
        angle={'up':0,'down':math.pi,'right':math.pi/2,'left':-math.pi/2}[direction]
        self.line([(x+a*math.cos(angle)-b*math.sin(angle),y+a*math.sin(angle)+b*math.cos(angle)) for a,b in p])
    def gear(self,x=32,y=31,r=10):
        for i in range(8):
            a=i*math.pi/4
            self.line([(x+math.cos(a)*r,y+math.sin(a)*r),(x+math.cos(a)*(r+3),y+math.sin(a)*(r+3))],w=4)
        self.circle(x,y,r,stroke=WHITE,w=4)
        self.circle(x,y,3,WHITE,None)
    def save(self,name):
        img=self.img.resize((64,64),Image.Resampling.LANCZOS)
        img.save(OUT/'icons'/f'{name}.png',optimize=True)
        (OUT/'Source'/f'{name}.svg').write_text('<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">'+''.join(self.svg)+'</svg>',encoding='utf-8')
        return img

names={
 'about':'blue','add':'green','animation-layers':'purple','animation-library':'purple',
 'batch-processing':'orange','camera-view':'gray','cast-preview':'blue','delete':'red',
 'dual-wield':'purple','export-animation':'orange','fit-view':'gray','hand-pose':'orange',
 'import-assets':'orange','inverse-kinematics':'green','language':'blue','model-parts':'blue',
 'move-down':'gray','move-up':'gray','next-frame':'green','notification':'orange',
 'output-naming':'blue','output-settings':'gray','pause':'green','play':'green',
 'previous-frame':'green','project-workspace':'orange','restore-layout':'gray','save':'blue',
 'save-as':'blue','timeline-playback':'purple','weapon-follow':'green',
 'weapon-processing-mode':'gray','zoom-in':'gray','zoom-out':'gray'}
images={}
for name,c in names.items():
    v=Vector(COLORS[c])
    if name=='about':
        v.circle(32,31,14); v.circle(32,24,1.8,WHITE,None); v.line([(32,30),(32,40)])
    elif name=='add':
        v.line([(21,31),(43,31)],w=4); v.line([(32,20),(32,42)],w=4)
    elif name=='animation-layers':
        for y in [23,31,39]:
            v.poly([(18,y),(32,y-7),(46,y),(32,y+7)],'#D9BDE0' if y==39 else '#E9D7ED' if y==31 else WHITE)
    elif name=='animation-library':
        v.rect(17,18,47,44,3,None,WHITE); v.poly([(28,25),(39,31),(28,37)])
        for x in [20,44]:
            for y in [23,31,39]: v.rect(x-1,y-1,x+1,y+1,0,WHITE)
    elif name=='batch-processing':
        for x,y in [(18,20),(32,20),(18,34)]:v.rect(x,y,x+10,y+10,2,None,WHITE,2)
        v.arrow('right',40,40)
    elif name=='camera-view':
        v.rect(17,22,47,42,4,None,WHITE); v.poly([(23,22),(27,17),(37,17),(41,22)])
        v.circle(32,32,7)
    elif name in ['model-parts','cast-preview']:
        v.poly([(32,17),(46,24),(32,31),(18,24)],'#D9F1F6')
        v.poly([(18,26),(30,33),(30,46),(18,39)],WHITE)
        v.poly([(34,33),(46,26),(46,39),(34,46)],'#AAD6E4')
        if name=='cast-preview': v.poly([(27,24),(38,29),(27,34)],COLORS[c])
    elif name=='delete':
        v.line([(20,23),(44,23)]);v.line([(26,18),(38,18)])
        v.line([(23,27),(25,44),(39,44),(41,27)])
        v.line([(29,29),(29,39)],w=2);v.line([(35,29),(35,39)],w=2)
    elif name=='dual-wield':
        for x in [19,35]:
            v.poly([(x,19),(x+11,19),(x+11,25),(x+7,25),(x+7,40),(x+2,44),(x,40)])
    elif name in ['export-animation','import-assets']:
        v.line([(18,34),(18,44),(46,44),(46,34)])
        v.arrow('up' if name=='export-animation' else 'down',32,28)
    elif name=='fit-view':
        for pts in [[(18,27),(18,18),(27,18)],[(37,18),(46,18),(46,27)],[(46,36),(46,45),(37,45)],[(27,45),(18,45),(18,36)]]:v.line(pts)
    elif name=='hand-pose':
        v.line([(23,36),(23,26),(27,26),(27,19),(31,19),(31,17),(35,17),(35,22),(39,22),(39,27),(43,27),(43,37),(38,45),(28,45),(19,36),(19,31),(23,34)],w=2.8)
    elif name=='inverse-kinematics':
        v.line([(20,41),(33,21),(44,39)],w=3)
        for x,y in [(20,41),(33,21),(44,39)]:v.circle(x,y,4,COLORS[c],WHITE)
    elif name=='language':
        v.circle(32,31,14);v.line([(18,31),(46,31)],w=2)
        v.line([(32,17),(25,24),(25,38),(32,45),(39,38),(39,24),(32,17)],w=2)
    elif name in ['move-down','move-up']:v.arrow('down' if name=='move-down' else 'up')
    elif name in ['play','next-frame','previous-frame','pause']:
        if name=='pause':
            v.rect(23,20,29,42,1,WHITE);v.rect(35,20,41,42,1,WHITE)
        elif name=='previous-frame':
            v.poly([(42,20),(25,31),(42,42)]);v.line([(21,20),(21,42)],w=3)
        else:
            v.poly([(24,20),(42,31),(24,42)])
            if name=='next-frame':v.line([(45,20),(45,42)],w=3)
    elif name=='notification':
        v.line([(19,39),(23,34),(23,25),(27,19),(37,19),(41,25),(41,34),(45,39),(19,39)])
        v.line([(28,44),(36,44)])
    elif name=='output-naming':
        v.line([(18,40),(25,21),(32,40)]);v.line([(21,33),(29,33)])
        v.line([(37,23),(44,23),(40,23),(40,41),(36,41),(44,41)],w=2)
    elif name in ['output-settings','weapon-processing-mode']:
        v.gear()
        if name=='weapon-processing-mode':v.line([(18,44),(46,18)],'#FFE2AF',3)
    elif name=='project-workspace':
        v.poly([(16,24),(16,19),(28,19),(32,24),(47,24),(47,44),(16,44)],'#FBD6A8')
        v.rect(16,26,47,44,3,WHITE)
        v.line([(21,33),(40,33)],COLORS[c],2);v.line([(21,38),(33,38)],COLORS[c],2)
    elif name=='restore-layout':
        v.rect(18,18,46,44,3,None,WHITE);v.line([(18,26),(46,26)],w=2);v.line([(27,26),(27,44)],w=2)
        v.line([(43,34),(35,34),(35,39)],'#FFDCB8',2)
    elif name in ['save','save-as']:
        v.poly([(18,18),(42,18),(47,23),(47,45),(18,45)])
        v.rect(25,18,39,28,0,COLORS[c]);v.rect(24,34,41,45,1,COLORS[c])
        if name=='save-as':v.line([(33,43),(45,31)],'#FFD28A',5)
    elif name=='timeline-playback':
        v.line([(18,40),(46,40)],w=2)
        for x in [20,28,36,44]:v.line([(x,38),(x,44)],w=2)
        v.poly([(27,18),(41,26),(27,34)])
    elif name=='weapon-follow':
        v.circle(37,27,10);v.circle(37,27,3,WHITE,None)
        v.line([(17,44),(23,36),(30,40)],'#FFE0AE',3);v.line([(23,36),(30,29)],'#FFE0AE',3)
    elif name in ['zoom-in','zoom-out']:
        v.circle(28,27,10);v.line([(36,35),(46,45)],w=4)
        v.line([(23,27),(33,27)],w=2)
        if name=='zoom-in':v.line([(28,22),(28,32)],w=2)
    images[name]=v.save(name)

with zipfile.ZipFile(OUT/'icons.zip','w',zipfile.ZIP_DEFLATED) as z:
    for name in names:z.write(OUT/'icons'/f'{name}.png',f'{name}.png')

# A real asset catalogue, not an app mock-up.
sheet=Image.new('RGB',(1120,730),'#F5F4F2');d=ImageDraw.Draw(sheet)
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',14)
title=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',30)
d.text((30,22),'Ubuntu / Yaru-inspired · 34 functional icons',font=title,fill='#292929')
d.text((30,64),'64 × 64 transparent PNG · original vector sources · light / dark visibility',font=font,fill='#66615D')
for i,(name,img) in enumerate(images.items()):
    x=24+(i%7)*156;y=110+(i//7)*120
    d.rounded_rectangle((x,y,x+144,y+108),radius=7,fill='#E6E3DF')
    d.rounded_rectangle((x+75,y+5,x+139,y+69),radius=5,fill='#303030')
    sheet.paste(img,(x+7,y+5),img);sheet.paste(img,(x+75,y+5),img)
    label=name.replace('-',' ')
    words=label.split();line='';rows=[]
    for word in words:
        if d.textlength((line+' '+word).strip(),font=font)>137:
            rows.append(line);line=word
        else:line=(line+' '+word).strip()
    rows.append(line)
    for j,row in enumerate(rows):d.text((x+5,y+73+j*16),row,font=font,fill='#35312E')
sheet.save(OUT/'icons-preview.png')
print(f'Created {len(images)} PNGs, SVG sources, icons.zip and catalogue in {OUT}')
