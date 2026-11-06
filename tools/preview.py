"""Mock of the main window (Barn tab) and the HUD using the real art. Fonts are stand-ins for the game's Jupiter/TrumpGothic/Axis."""
import os, sys
from PIL import Image, ImageDraw, ImageFont
A = sys.argv[1]; OUT = sys.argv[2]
T = os.path.dirname(os.path.abspath(__file__))
SER = os.path.join(T, "fonts", "YoungSerif-Regular.ttf")
CAP = "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf"
SANS = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
f = lambda p, s: ImageFont.truetype(p, s)
INK=(92,58,66); SOFT=(140,112,115); ROSE=(168,98,112); BLUE=(98,128,162); SAGE=(106,138,110); HONEY=(176,138,78); LINEN=(233,224,210)
def im(p): return Image.open(A+"/"+p).convert("RGBA")

class Canvas:
    def __init__(s, img): s.img=img; s.d=ImageDraw.Draw(img)
    def nine(s, tex, box, tb, sb):
        x0,y0,x1,y1=[int(v) for v in box]; tw,th=tex.size; sb=int(sb)
        xs=[x0,x0+sb,x1-sb,x1]; ys=[y0,y0+sb,y1-sb,y1]; us=[0,tb,tw-tb,tw]; vs=[0,tb,th-tb,th]
        for i in range(3):
            for j in range(3):
                w=xs[i+1]-xs[i]; h=ys[j+1]-ys[j]
                if w>0 and h>0: s.img.alpha_composite(tex.crop((us[i],vs[j],us[i+1],vs[j+1])).resize((w,h)),(xs[i],ys[j]))
    def card(s, box, blue=False): s.nine(im("ui/card_blue.png" if blue else "ui/card.png"), box, 48, 34)
    def icon(s, n, c, r, a=1.0):
        ic=im(f"icons/{n}.png").resize((r*2,r*2))
        if a<1: ic.putalpha(ic.getchannel("A").point(lambda v:int(v*a)))
        s.img.alpha_composite(ic,(int(c[0]-r),int(c[1]-r)))
    def text(s, t, xy, fnt, col, anchor="la"): s.d.text(xy, t, font=fnt, fill=col, anchor=anchor)
    def eyebrow(s, t, xy, w, col=ROSE):
        s.text(t.upper(), xy, f(CAP,15), col); tw=s.d.textlength(t.upper(), font=f(CAP,15))
        s.d.line([(xy[0]+tw+10, xy[1]+9),(xy[0]+w, xy[1]+9)], fill=col+(90,), width=1)
    def button(s, label, box, color, enabled=True):
        s.nine(im(f"ui/button_{color if enabled else 'cream'}.png"), box, 40, int((box[3]-box[1])*0.45))
        s.text(label.upper(), ((box[0]+box[2])/2,(box[1]+box[3])/2-1), f(CAP,14), INK if enabled else SOFT, "mm")
    def bar(s, box, t, col):
        r=(box[3]-box[1])/2; s.d.rounded_rectangle(box, r, fill=LINEN)
        if t>0: s.d.rounded_rectangle([box[0],box[1],box[0]+(box[2]-box[0])*t,box[3]], r, fill=col)
    def bottle(s, tier, xy, w, h, frac):
        tops={1:.20,2:.27,3:.21,4:.17,5:.444}; bots={1:.96,2:.96,3:.96,4:.96,5:.902}
        x,y=xy
        s.img.alpha_composite(im(f"ui/bottle{tier}_back.png").resize((w,h)),(x,y))
        lvl=bots[tier]-(bots[tier]-tops[tier])*frac; cy=int(h*lvl)
        s.img.alpha_composite(im(f"ui/bottle{tier}_milk.png").resize((w,h)).crop((0,cy,w,h)),(x,y+cy))
        s.img.alpha_composite(im(f"ui/bottle{tier}_front.png").resize((w,h)),(x,y))
    def seal(s, c, r, letter, col):
        s.img.alpha_composite(im("ui/seal.png").resize((r*2,r*2)),(int(c[0]-r),int(c[1]-r)))
        s.text(letter, (c[0], c[1]+1), f(SER, int(r*0.9)), col, "mm")
    def row(s, x, y, w, icon, title, lines, buttons, h=None):
        s.icon(icon,(x+22,y+28),20)
        s.text(title,(x+54,y+6),f(SER,18),INK)
        yy=y+32
        for t,c in lines:
            s.text(t,(x+54,yy),f(SANS,12),c); yy+=17
        bx=x+w
        for lab,col,en in reversed(buttons):
            bw=max(86, s.d.textlength(lab.upper(),font=f(CAP,14))+34); bx-=bw
            s.button(lab,[bx,y+(yy-y-34)/2+4,bx+bw,y+(yy-y-34)/2+38],col,en); bx-=8
        bottom=max(yy, y+48)+8
        s.d.line([(x,bottom),(x+w,bottom)],fill=ROSE+(40,)); return bottom+6

W,H=1060,760
img=im("bg.png").resize((W,H)); c=Canvas(img); d=c.d
# header
d.rectangle([0,0,W,92], fill=(251,247,238,235)); d.line([(0,88),(W,88)], fill=ROSE, width=2); d.line([(0,91),(W,91)], fill=ROSE+(80,), width=1)
img.alpha_composite(im("avatars/you.png").resize((60,60)),(18,14))
c.text("VELVET",(92,14),f(CAP,15),ROSE); c.text("DAIRY",(91,32),f(CAP,34),INK)
c.text("Level 4 · Dairy Cow",(242,24),f(SER,21),INK); c.bar([242,56,482,63],0.46,ROSE); c.text("98 / 230 xp",(242,66),f(SANS,12),SOFT)
c.icon("coin",(W-18-48-22,36),16); c.text("612",(W-18,24),f(SANS,21),INK,"ra")
chip="IN ROSE HOLLOW DAIRY."; cw=d.textlength(chip,font=f(CAP,14)); x0=W-18-cw-30
d.rounded_rectangle([x0,56,W-18,78],11,fill=(232,240,230),outline=SAGE); d.ellipse([x0+8,64,x0+15,71],fill=SAGE); c.text(chip,(x0+20,59),f(CAP,14),SAGE)
x=18
for i,t in enumerate(["BARN","CREAMERY","SHOP","DAIRY","PROGRESS","SETTINGS"]):
    tw=d.textlength(t,font=f(CAP,15)); bw=tw+28
    if i==0: d.rounded_rectangle([x,100,x+bw,134],8,fill=(251,247,238,220)); d.line([(x+10,132),(x+bw-10,132)],fill=ROSE,width=2)
    c.text(t,(x+bw/2,117),f(CAP,15),INK if i==0 else SOFT,"mm"); x+=bw+4
top=146; bot=H-18; milkH=200; topBot=bot-milkH-14
# bottle card
c.card([18,top,288,topBot]); bh=int(min(max(topBot-top-150,150),380)); bw=bh//2; bx=18+(270-bw)//2-8; by=top+26
c.bottle(1,(bx,by),bw,bh,0.61)
c.text("+1 OZ",(bx+bw+4,by+bh*(0.96-0.70*0.61)-24),f(CAP,14),SAGE)
c.seal((bx+bw+6,by+bh*0.3),28,"C",(138,130,124))
cx=153; yy=by+bh+6
c.text("4.9",(cx-6,yy),f(SANS,34),INK,"ra"); c.text("/ 8 oz",(cx,yy+12),f(SANS,17),SOFT)
c.text("+0.11 OZ A MINUTE",(cx,yy+46),f(CAP,14),SOFT,"ma")
c.text("Resting · Contented",(cx,yy+70),f(SANS,12),SAGE,"ma")
c.text("Milk bottle · 8 oz",(cx,yy+92),f(SANS,12),SOFT,"ma")
# needs
mx0=302; mx1=W-18-330-14; c.card([mx0,top,mx1,topBot]); c.eyebrow("How you're doing",(mx0+28,top+24),mx1-mx0-56)
gy=top+56; gw=mx1-mx0-56
for n,l,v,col in [("hay","Hunger",82,HONEY),("water","Thirst",77,BLUE),("cushion","Comfort",91,ROSE),("clover","Mood",80,SAGE),("premium","Feed quality",46,HONEY)]:
    c.icon(n,(mx0+28+17,gy+19),17); xx=mx0+28+44; ww=gw-44
    c.text(l.upper(),(xx,gy-1),f(CAP,14),SOFT); c.text(str(v),(xx+ww,gy),f(SANS,13),INK,"ra"); c.bar([xx,gy+24,xx+ww,gy+32],v/100,col); gy+=46
c.icon("ribbon",(mx0+28+17,gy+22),17); xx=mx0+28+44
c.text("MILK QUALITY - GRADE C",(xx,gy-1),f(CAP,14),SOFT); c.bar([xx,gy+24,mx0+28+gw,gy+32],0.6,(106,138,110))
c.text("90 / 150 to Grade B",(xx,gy+38),f(SANS,12),SOFT); gy+=62
c.text("All good. Keep it up for better milk.",(mx0+28,gy),f(SANS,13),SAGE)
# care
cx0=W-18-330; c.card([cx0,top,W-18,topBot]); c.eyebrow("Care",(cx0+28,top+24),330-56)
ry=top+56; rw=330-56-12
for icon,t,sub,b,col,en in [("water","Drink","Free · +40 thirst","Drink","blue",True),("cushion","Stretch","Ready in 3m 12s","Stretch","rose",False),
                    ("hay","Meadow hay","4 left · +30 hunger","Eat","honey",True),("biscuit","Cream biscuit","2 left · ready in 2h 41m","Eat","honey",False)]:
    if ry+60>topBot-20: break
    ry=c.row(cx0+28,ry,rw,icon,t,[(sub,SOFT)],[(b,col,en)])
d.rounded_rectangle([W-18-36,top+60,W-18-32,topBot-30],2,fill=ROSE+(70,))
# milking
c.card([18,bot-milkH,W-18,bot],True); c.eyebrow("Milking",(46,bot-milkH+24),W-36-56,BLUE)
o=(46,bot-milkH+60); colw=(W-36-56-20)/2
c.row(o[0],o[1],colw,"bucket","Stalls",[("Sit down in one of your stalls.",SOFT),("Sitting in a stall starts it by itself.",SOFT)],[("Start","blue",False)])
fx=o[0]+colw+20
c.row(fx,o[1],colw,"ribbon","Hand milking",[("Better grade and more xp. Pick who milks you:",SOFT)],[])
c.button("Juniper",[fx+54,o[1]+64,fx+54+110,o[1]+98],"sage")
main=img

# HUD
hud=Image.new("RGBA",(330,168),(0,0,0,0)); hc=Canvas(hud)
hc.card([0,0,330,168])
bh=134; hc.bottle(1,(20,17),bh//2,bh,0.61)
x=20+bh//2+14
hc.text("4.9",(x,18),f(SANS,19),INK); hc.text("/ 8 oz",(x+36,24),f(SANS,12),SOFT)
hc.seal((330-38,38),20,"C",(138,130,124))
bw=330-x-26; half=(bw-10)/2
for i,(l,v,col) in enumerate([("Hunger",82,HONEY),("Thirst",77,BLUE),("Comfort",91,ROSE),("Mood",80,SAGE)]):
    bx=x+(i%2)*(half+10); by=52+(i//2)*28
    hc.text(l,(bx,by),f(SER,12),SOFT); hc.bar([bx,by+16,bx+half,by+21],v/100,col)
hc.text("Resting in the dairy",(x,168-46),f(SER,13),SAGE)

# compose: main window on a dim game-like backdrop with the HUD beside it
canvas=Image.new("RGBA",(W+330+60,H+40),(46,40,48,255))
canvas.alpha_composite(main,(20,20))
canvas.alpha_composite(hud,(W+40,20))
ImageDraw.Draw(canvas).text((W+40,200),"HUD",font=f(CAP,16),fill=(220,210,215))
canvas.convert("RGB").save(OUT)
