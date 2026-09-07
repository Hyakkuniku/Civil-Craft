from pathlib import Path
import re
from html import escape
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_LEFT
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, CondPageBreak
from reportlab.lib.pagesizes import letter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'output/pdf/Civil_Craft_NPC_Phase_Story_Team_Guide.pdf'
OUT.parent.mkdir(parents=True, exist_ok=True)
for name, file in [('Guide','arial.ttf'),('GuideBold','arialbd.ttf'),('GuideItalic','ariali.ttf'),('GuideBoldItalic','arialbi.ttf')]:
    pdfmetrics.registerFont(TTFont(name, str(Path('C:/Windows/Fonts') / file)))
pdfmetrics.registerFontFamily('Guide',normal='Guide',bold='GuideBold',italic='GuideItalic',boldItalic='GuideBoldItalic')
body = ParagraphStyle('Body',fontName='Guide',fontSize=10.5,leading=14.3,spaceAfter=7,textColor=colors.HexColor('#252525'),splitLongWords=True,allowWidows=0,allowOrphans=0)
styles = {
    'body': body,
    'title': ParagraphStyle('Title',parent=body,fontName='GuideBold',fontSize=26,leading=31,spaceAfter=13,textColor=colors.black,keepWithNext=True),
    'h2': ParagraphStyle('H2',parent=body,fontName='GuideBold',fontSize=16,leading=20,spaceBefore=16,spaceAfter=8,textColor=colors.black,keepWithNext=True),
    'h3': ParagraphStyle('H3',parent=body,fontName='GuideBold',fontSize=12,leading=16,spaceBefore=10,spaceAfter=6,textColor=colors.black,keepWithNext=True),
    'bullet': ParagraphStyle('Bullet',parent=body,leftIndent=13,firstLineIndent=-10,spaceAfter=4),
    'quote': ParagraphStyle('Quote',parent=body,leftIndent=15,fontName='GuideItalic'),
    'cell': ParagraphStyle('Cell',parent=body,fontSize=9.6,leading=12.7,spaceAfter=0),
    'tablehead': ParagraphStyle('TableHead',parent=body,fontName='GuideBold',fontSize=9.6,leading=12.7,textColor=colors.white,spaceAfter=0),
}

def inline(text):
    text = text.replace('\u2011','-').replace('\u2013','-').replace('\u2014','-')
    text = escape(text)
    text = re.sub(r'\[([^\]]+)\]\(#([^\)]+)\)',r'<link href="#\2" color="#30516A">\1</link>',text)
    text = re.sub(r'\*\*(.+?)\*\*',r'<b>\1</b>',text)
    text = re.sub(r'`([^`]+)`',r'<font color="#40536A">\1</font>',text)
    return text

def slug(text):
    return re.sub(r'[^a-z0-9 -]','',text.lower()).replace(' ','-')

class GuideDoc(SimpleDocTemplate):
    def afterFlowable(self, flow):
        if isinstance(flow,Paragraph) and flow.style.name=='H2':
            title=flow.getPlainText()
            self.canv.bookmarkPage(slug(title))
            self.canv.addOutlineEntry(title,slug(title),0,False)

def page_footer(canvas,doc):
    canvas.saveState()
    canvas.setFont('Guide',8)
    canvas.setFillColor(colors.HexColor('#666666'))
    canvas.drawString(54,27,'Civil Craft  |  NPC phase authoring')
    canvas.drawRightString(558,27,str(doc.page))
    canvas.restoreState()

lines=(ROOT/'Documentation/NPC_Phases_Story_Team_Guide.md').read_text(encoding='utf-8').splitlines()
story=[]
i=0
while i<len(lines):
    line=lines[i].strip()
    if not line:
        i+=1; continue
    if line.startswith('|'):
        rows=[]
        while i<len(lines) and lines[i].strip().startswith('|'):
            cells=[c.strip() for c in lines[i].strip().strip('|').split('|')]
            if not all(re.fullmatch(r':?-+:?',c) for c in cells): rows.append(cells)
            i+=1
        count=len(rows[0])
        widths={2:[155,349],3:[125,172,207],4:[45,132,203,124]}[count]
        data=[[Paragraph(inline(c),styles['tablehead' if row==0 else 'cell']) for c in cells] for row,cells in enumerate(rows)]
        table=Table(data,colWidths=widths,repeatRows=1,hAlign='LEFT')
        table.setStyle(TableStyle([
            ('BACKGROUND',(0,0),(-1,0),colors.HexColor('#334B5D')),
            ('ROWBACKGROUNDS',(0,1),(-1,-1),[colors.white,colors.HexColor('#F3F5F7')]),
            ('GRID',(0,0),(-1,-1),.5,colors.HexColor('#D9D9D9')),
            ('VALIGN',(0,0),(-1,-1),'MIDDLE'),
            ('LEFTPADDING',(0,0),(-1,-1),7),('RIGHTPADDING',(0,0),(-1,-1),7),
            ('TOPPADDING',(0,0),(-1,-1),7),('BOTTOMPADDING',(0,0),(-1,-1),7),
        ]))
        story.extend([Spacer(1,3),table,Spacer(1,10)])
        continue
    if line.startswith('# '):
        story.append(Paragraph(inline(line[2:]),styles['title']))
    elif line.startswith('## '):
        title=line[3:]
        story.append(CondPageBreak(130))
        story.append(Paragraph('<a name="'+slug(title)+'"/>'+inline(title),styles['h2']))
    elif line.startswith('### '):
        if line[4:] == 'Implementation references':
            story.append(CondPageBreak(280))
        story.append(Paragraph(inline(line[4:]),styles['h3']))
    elif line.startswith('- [ ] '):
        story.append(Paragraph('[  ] '+inline(line[6:]),styles['bullet']))
    elif line.startswith('- '):
        story.append(Paragraph('• '+inline(line[2:]),styles['bullet']))
    elif re.match(r'^\d+\. ',line):
        story.append(Paragraph(inline(line),styles['bullet']))
    elif line.startswith('>'):
        if line[1:].strip(): story.append(Paragraph(inline(line[1:].strip()),styles['quote']))
    else:
        story.append(Paragraph(inline(line),styles['body']))
    i+=1

doc=GuideDoc(str(OUT),pagesize=letter,rightMargin=54,leftMargin=54,topMargin=48,bottomMargin=48,
            title='Civil Craft NPC Phase Authoring Guide',author='Civil Craft',allowSplitting=1)
doc.build(story,onFirstPage=page_footer,onLaterPages=page_footer)
print(OUT)
