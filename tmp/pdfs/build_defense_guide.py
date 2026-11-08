from pathlib import Path
from xml.sax.saxutils import escape
import json
from reportlab.pdfgen import canvas
from reportlab.lib.colors import HexColor, white
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import Paragraph, Spacer, Table, TableStyle, KeepTogether
from reportlab.lib.enums import TA_LEFT
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from pypdf import PdfReader
import pypdfium2 as pdfium
from PIL import Image, ImageOps, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'output/pdf/Civil_Craft_Pre_Oral_Cram_and_Panel_QA.pdf'
TMP = ROOT / 'tmp/pdfs'
OUT.parent.mkdir(parents=True, exist_ok=True)
for name, file in [('Arial','arial.ttf'),('Arial-Bold','arialbd.ttf'),('Arial-Italic','ariali.ttf')]:
    pdfmetrics.registerFont(TTFont(name, str(Path('C:/Windows/Fonts')/file)))
pdfmetrics.registerFontFamily('Arial', normal='Arial', bold='Arial-Bold', italic='Arial-Italic', boldItalic='Arial-Bold')
W,H = 595.276,841.89
LEFT, RIGHT, TOP, BOTTOM = 44,44,746,52
CW=W-LEFT-RIGHT
INK=HexColor('#172B3A'); TEAL=HexColor('#087F80'); MUTED=HexColor('#566777'); LIGHT=HexColor('#EDF5F5'); LINE=HexColor('#D8E3E7'); AMBER=HexColor('#FFF3DC')
styles={
 'body':ParagraphStyle('body',fontName='Arial',fontSize=10.3,leading=14.3,textColor=INK,spaceAfter=8),
 'small':ParagraphStyle('small',fontName='Arial',fontSize=8.7,leading=12,textColor=MUTED,spaceAfter=6),
 'sub':ParagraphStyle('sub',fontName='Arial-Bold',fontSize=12,leading=15,textColor=TEAL,spaceBefore=10,spaceAfter=6),
 'q':ParagraphStyle('q',fontName='Arial-Bold',fontSize=11.4,leading=15.4,textColor=INK,spaceAfter=4),
 'answer':ParagraphStyle('answer',fontName='Arial',fontSize=11.1,leading=15.2,textColor=INK,spaceAfter=14),
 'cell':ParagraphStyle('cell',fontName='Arial',fontSize=9.2,leading=12.7,textColor=INK),
 'cellhead':ParagraphStyle('cellhead',fontName='Arial-Bold',fontSize=9.3,leading=12.5,textColor=white),
 'quote':ParagraphStyle('quote',fontName='Arial',fontSize=10.7,leading=15,textColor=INK),
}
def p(text,style='body'): return Paragraph(text,styles[style])
def sub(text): return p(text,'sub')
def box(text, warning=False):
    t=Table([[p(text,'quote')]],colWidths=[CW])
    t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,-1),AMBER if warning else LIGHT),('BOX',(0,0),(-1,-1),.6,LINE),('LEFTPADDING',(0,0),(-1,-1),12),('RIGHTPADDING',(0,0),(-1,-1),12),('TOPPADDING',(0,0),(-1,-1),11),('BOTTOMPADDING',(0,0),(-1,-1),11)]))
    return [t,Spacer(1,9)]
def table(headers, rows, widths):
    data=[[p(h,'cellhead') for h in headers]]+[[p(c,'cell') for c in row] for row in rows]
    t=Table(data,colWidths=widths,hAlign='LEFT')
    t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),TEAL),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),9),('RIGHTPADDING',(0,0),(-1,-1),9),('TOPPADDING',(0,0),(-1,-1),7),('BOTTOMPADDING',(0,0),(-1,-1),7),('ROWBACKGROUNDS',(0,1),(-1,-1),[white,LIGHT]),('LINEBELOW',(0,0),(-1,0),.5,TEAL)]))
    return t

pages=[]
def add(title,kicker,items): pages.append((title,kicker,items))

add('Read this first','01 / FIVE-MINUTE CRAM',[
 p('Civil Craft: Bridge Edition','sub'),
 *box('<b>One-sentence answer:</b> Civil Craft is an Android-based 3D bridge construction game that supports basic structural mechanics learning through guided contracts, material and budget decisions, load tests, lesson review, and quizzes.'),
 sub('The problem, purpose, and audience'),
 p('<b>Problem:</b> Students can find it difficult to visualize how bridge structures respond to loads using static explanations alone.<br/><b>Purpose:</b> Provide an interactive supplement to instruction through a build-test-improve cycle.<br/><b>Target learners:</b> Civil engineering students at TUP Cavite.'),
 sub('Memorize these six distinctions'),
 table(['Say this','What it means'],[
 ('3D world; planar bridge design','The environment is 3D, while construction and structural analysis use a 2D plane.'),
 ('Custom solver + Unity PhysX','The solver calculates simplified axial forces; PhysX handles motion and collisions.'),
 ('Stress percentage = utilization','Calculated member force divided by the applicable force capacity, multiplied by 100.'),
 ('Fixed bridge width','A predefined task condition; editable width needs additional structural modeling.'),
 ('Photon Fusion + PlayFab','Photon handles real-time sessions. PlayFab handles accounts, cloud records and leaderboard data.'),
 ('Learning support','Positive ratings indicate perceived usefulness; learning gains require suitable learning evidence.')],[159,CW-159]),
 sub('A 30-second opening'),
 p('"Our project addresses the difficulty of visualizing structural behavior. Players receive a bridge contract, inspect its load, choose materials, build within a budget, and test their design. Stress feedback and failure allow them to revise their work, while lessons and quizzes connect the experience to basic structural mechanics. The game uses simplified calculations for conceptual learning."'),
 p('<b>Study order:</b> Read pages 1-3 first. Practice the 42 panel answers on pages 4-9. Use page 10 to check your paper and demo before presenting.','small')
])

add('Feature and demo map','02 / SHOW THE LEARNING CYCLE',[
 p('Use prepared saves so you can show one complete task without playing the entire story. Feature availability must match the APK and account used for the demonstration.'),
 table(['Show','What to explain'],[
 ('Account access','Register, login, account recovery and guest access. Local guest play and account cloud features serve different needs.'),
 ('Story and guidance','NPC conversations, objectives, tutorial controls and regional progression provide context for construction tasks.'),
 ('Contract and inspection','Read the budget, permitted materials and load requirements. Inspect vehicle mass; show cargo only on a cargo-enabled task.'),
 ('Construction tools','Place and connect members. Show material information, grid snapping, moving/deleting, undo/redo and the changing budget.'),
 ('Testing and retry','Run a rehearsed weak design, explain the feedback, reinforce it, and run a successful crossing.'),
 ('Results and records','Show cost and peak stress, save the bridge, then perform the contract turn-in and reward collection.'),
 ('Multiple vehicles','Sequential = one after another. Together = overlapping traffic loads. Every required vehicle must finish.'),
 ('Learning support','Open lesson introductions, material references, Almanac records, a quiz result and its answer review.'),
 ('Progress and identity','Show achievements, available shop items, owned cosmetics, character customization and selected leaderboards.'),
 ('Online and website','Host/join with two devices; show chat and a prepared competition. Show the account dashboard, records and admin content tools.')],[116,CW-116]),
 sub('A short demo narration'),
 p('"This contract defines the problem. Vehicle inspection identifies the live load. Our material choices affect weight, capacity and cost. The first test exposes a weak design. After revising the supports, we test again and compare results. We then review the related lesson and assessment feedback."'),
 p('<b>Website/payment cue:</b> Use prepared account records. Identify payment tests as tests, and describe only features confirmed in the deployed environment.','small')
])

add('How your stress calculation works','03 / THE TECHNICAL ANSWER',[
 sub('1. Convert the design into a structural model'),
 p('The game reads node positions, connections, member lengths and angles, material properties, and fixed supports. The current solver is a deterministic, quasi-static 2D truss model. It primarily calculates axial tension and compression.'),
 sub('2. Apply dead and live loads'),
 p('<b>Dead load:</b> Member mass is based on placed length and mass per metre. Its weight is shared between its endpoints.<br/><b>Live load:</b> The supported vehicle/cargo mass is applied along the road based on its position. Multiple-load tests combine supported vehicle contributions.<br/><b>Conversion:</b> Force in newtons = mass in kilograms x 9.81.'),
 sub('3. Solve member forces'),
 p('The solver assembles a stiffness matrix and solves <b>K u = F</b>: K represents the connected structure, u represents calculated node displacements, and F represents applied loads. It then obtains each member\'s axial force. The stiffness values are simplified gameplay values related to configured capacity, rather than measured material elasticity and section properties.'),
 *box('<b>Member utilization (%) = |axial force| / applicable force capacity x 100</b><br/>Example: 6,000 N / 10,000 N x 100 = <b>60%</b>.<br/>Use the tension limit for pulling and the compression limit for pushing.'),
 p('Ropes act in tension only. Configured pier compression capacity reduces with increased height. In normal capacity-based failure, a ratio of 1.0 (100%) or more can trigger member breakage. Deformation and impacts provide additional failure or warning behavior.'),
 sub('What the numbers mean'),
 p('<b>Stress percentage:</b> Force-to-capacity utilization, not an MPa measurement. Engineering axial stress is sigma = force / cross-sectional area.<br/><b>Peak stress:</b> The recorded highest tracked stress value, rather than an average across the entire bridge.<br/><b>Estimated safety:</b> Estimated single-load capacity / specified live-load mass. The scalar estimate is hidden for multiple-vehicle tests.'),
 p('<b>Keep the limit clear:</b> The model does not independently calculate a full 3D bending, shear and torsion response. PhysX movement and lesson coverage should not be described as proof of full engineering analysis.','small')
])

qa_sections=[
('Design choices and scope','04 / QUESTIONS 1-7',[
('Why can players not edit bridge width?', 'Width is a predefined condition of each task. Our builder and solver work in a 2D plane, so players focus on member placement, supports, materials and cost. Editable width would require explicit modeling of transverse load distribution, deck behavior and torsion. It is a possible future extension.'),
('Does width affect a real bridge\'s strength?', 'Yes. Width can affect self-weight, load distribution, stiffness and torsional behavior. We acknowledge those effects, but our current design does not expose width as an independent variable. We should describe width as fixed within a simplified model, rather than claim that width has no effect.'),
('Why call the game 3D when the analysis is 2D?', 'The environment, characters, vehicles and bridge presentation are three-dimensional. Bridge drawing and structural analysis occur on a two-dimensional plane. These are different aspects of the system, and we state both to explain exactly what the game represents.'),
('Why use a simplified truss model?', 'It provides a manageable way to connect geometry, supports and loads with axial member demand. It supports the game\'s intended conceptual tasks within the development scope. It also has limitations for beam bending and full 3D behavior, which we disclose rather than treating it as a universal bridge model.'),
('Are beam, truss, arch and suspension bridges equally realistic?', 'They are covered as structural forms through lessons and construction scenarios. Their presence does not mean the solver reproduces all behavior of each form. The numerical model remains simplified and planar, with axial member analysis and tension-only ropes.'),
('Are wind and earthquakes implemented?', 'They are future extensions, not features we can claim as working load scenarios in the reviewed implementation. The current demonstration focuses on self-weight and vehicle or cargo loads. Any remaining paper wording that says otherwise should be corrected.'),
('Can this replace structural design software?', 'No. It is a supplementary learning game. Its material capacities, stiffness assumptions and construction representation are simplified. It does not provide the validated code checks, detailed sections or comprehensive analyses required for professional structural design.')]),
('Stress, loads, and simulation','05 / QUESTIONS 8-14',[
('How do you obtain the bridge stress percentage?', 'We calculate axial forces using the custom planar solver, then divide each member\'s force magnitude by its applicable tension or compression capacity. Multiplying by 100 gives utilization. For example, a 6,000-newton force against a 10,000-newton capacity is 60 percent.'),
('Does PhysX calculate the stress score?', 'The normal configured path uses the custom deterministic solver for stress feedback, scoring inputs and capacity-based failure. PhysX handles motion, contacts and collisions. Additional deformation and fallback behavior exists, so we describe the division of responsibilities precisely.'),
('What is the difference between dead load and live load?', 'Dead load is the weight of the bridge members themselves. Live load is the vehicle or carried cargo involved in the test. The solver combines the loads supported by the structure and considers the live load\'s position along the road.'),
('How does the solver distribute the load?', 'It uses member geometry, connectivity, supports and simplified stiffness to solve the equilibrium system K u = F. It then calculates each member\'s axial force. This is more specific than assigning the same force to every member or using only total vehicle weight.'),
('How do you determine material strength?', 'Each material asset stores approximate tensile and compressive force capacities, mass per metre and cost. The game compares the calculated member force with the relevant configured capacity. Those values are simplified game parameters; their presence alone does not establish real-world calibration.'),
('What causes failure, and what do the colors mean?', 'Colors indicate increasing tracked demand or warning level. A member reaching its configured axial capacity can break under normal test rules. Excessive deformation, impacts and contract limits can also affect failure. A successful-looking bridge still needs to satisfy the full crossing conditions.'),
('Is your safety ratio a real engineering factor of safety?', 'It is an estimated gameplay ratio: estimated bridge capacity divided by the specified live-load mass. We label it as an estimate and hide it for multiple-vehicle tests. It is not an independently validated safety certification or a substitute for engineering design checks.')]),
('Purpose and educational value','06 / QUESTIONS 15-21',[
('What problem does your project address?', 'The project addresses difficulty visualizing structural response and connecting classroom concepts with design decisions. It provides an interactive bridge-building environment where students can change a design, observe feedback and relate the outcome to the concepts introduced in lessons.'),
('What is the main objective and who benefits?', 'The main objective is to develop a mobile learning-support tool for basic structural mechanics through interactive bridge construction. The intended learners are civil engineering students at TUP Cavite. Educators can use it for supplementary demonstrations and guided discussion.'),
('What distinguishes it from entertainment bridge games?', 'Our project combines an intended civil-engineering learning context with NPC-guided lessons, an Almanac, material references, quizzes and feedback linked to construction tasks. Its contribution is this educational design and implementation; we should not claim that bridge-building gameplay itself is new.'),
('How does repeated failure support learning?', 'Failure supplies feedback about a particular design attempt. Players can inspect the result, change support or material choices, and compare the next test. The educational opportunity comes from interpreting that feedback and reviewing related concepts, rather than from repeated failure alone.'),
('Why include an Almanac and quizzes?', 'The Almanac makes discovered concepts, material references and bridge records available for review. Quizzes provide questions and submitted-answer feedback. Together, these features create a more explicit connection between the construction activity and the engineering concepts being introduced.'),
('Why allow quiz progression at any score?', 'The current assessment design provides a score and answer review, then permits progression after submission. It is not a mastery gate. This keeps the story accessible, while leaving room for future remediation or mastery requirements if the team chooses to implement and evaluate them.'),
('Can you prove the game improves learning?', 'The strength of that claim depends on our evidence. User ratings can show perceived usefulness, usability and engagement. Demonstrating learning gains requires an appropriate learning assessment, such as aligned pre/post measures. We should report only what our actual study design and verified data support.')]),
('Methodology, evaluation, and evidence','07 / QUESTIONS 22-28',[
('Why did you use Agile development?', 'Iterative development let us refine mechanics, interfaces and learning flows in manageable cycles. We could test a feature, identify problems, and revise the implementation. During the defense, we should support that explanation with our actual sprint records, versions and testing evidence.'),
('Who evaluated the system?', 'The current paper describes 100 participants: 90 civil engineering students and two expert groups of five each. Before presenting, verify the actual recruitment records, institution, expert labels and completed response counts. Explain student and expert roles separately where they answer different evaluation questions.'),
('What evaluation criteria did you use?', 'We must name the criteria in the questionnaire actually administered and map them consistently to our objectives and results. The current paper names eight criteria in some sections but reports seven differently named dimensions elsewhere. That mismatch must be reconciled before memorizing a definitive list.'),
('What does accuracy mean in your evaluation?', 'The revised paper describes perceived conceptual correctness, rather than independently benchmarked numerical accuracy. Student opinions can assess whether feedback seems understandable and appropriate. Numerical accuracy needs separate comparisons with known solutions, and domain-expert review can assess the educational explanation.'),
('What are your overall rating and statistical results?', 'Use only the final figures verified against the raw responses. The current draft contains conflicting summary and table values, so this guide does not treat them as confirmed results. Prepare a corrected result sheet showing sample size, scale, means, standard deviations and interpretations.'),
('How would you validate the structural calculations?', 'A suitable validation plan would compare simple supported structures with hand calculations, check self-weight and load placement, verify tension-only rope behavior, and compare repeat runs. Then test gameplay failures and multiple loads. Identify completed checks separately from this proposed validation plan.'),
('What testing evidence can you present?', 'We can present the implementation checks, scenario tests and device records actually completed. A successful compilation or isolated test is not proof of a full Android playthrough or two-device session. We should identify the build, device, scenario, expected result and observed result for each claim.')]),
('Accounts, multiplayer, and the website','08 / QUESTIONS 29-35',[
('What does PlayFab do, and what does Photon do?', 'PlayFab supports player authentication, cloud records and leaderboard data. Photon Fusion handles real-time room creation, joining and networked sessions. The website adds account-facing pages and related services. We should avoid assigning every online function to a single provider.'),
('Can the game be played offline?', 'Guest story progress and learning activities can use local storage. Online sessions, cloud synchronization, leaderboards and account-wallet operations require connectivity where applicable. We should demonstrate the exact offline behavior of the prepared build rather than promise that every account feature works offline.'),
('How is the multiplayer competition scored?', 'A design must complete the crossing within budget to qualify. Qualified designs receive a combined score using 40 percent cost efficiency and 60 percent strength, based on peak stress. Equal displayed scores produce a draw. If neither qualifies, there is no winner and both outcomes are losses.'),
('Why use 40 percent cost and 60 percent strength?', 'These are the configured scoring weights, placing more emphasis on structural performance while still rewarding economical choices. Unless we have a documented study supporting the weights, we should describe them as a game-design decision rather than an experimentally proven optimum.'),
('How do you keep the competition comparable?', 'Both submitted designs use the same selected task and a frozen snapshot of load requirements. The host controls the test and completion process. Designs must satisfy the same qualification conditions. This supports consistency, but it is not a claim that the entire system is cheat-proof.'),
('Why does the project need a website?', 'The website provides game information, downloads, selected leaderboards and account-facing records such as available bridge and achievement data. Administrative tools support content and operational management. It extends access to records and information beyond the game rather than performing the bridge simulation itself.'),
('How do purchases, rewards and cosmetics fit the project?', 'Rewards support progression, and cosmetics allow personal expression. Account purchases and wallet balances use the implemented online services, while guests have local earning and spending behavior. For the demo, identify the actual test-payment environment and avoid claiming live commercial readiness without deployment evidence.')]),
('Limits, risks, and follow-up questions','09 / QUESTIONS 36-42',[
('Why Android, and what devices are supported?', 'Android is the target platform within our project scope. Supported-device claims must follow measured compatibility and performance tests. We have A12 profiling documentation, while the paper also names an A54. Confirm which devices were actually tested and distinguish them from minimum requirements.'),
('Is the game fast enough for students\' phones?', 'We should answer with measured results for the demonstrated build and device, including gameplay, construction and simulation. Earlier profiling does not establish current performance after later changes. We should disclose any remaining performance limits instead of generalizing from one device.'),
('How do you protect player data?', 'The system separates accounts and uses authenticated services for protected records. Specific safeguards should be supported by implementation evidence and testing. Account login alone does not prove complete security, and we should not claim compliance or comprehensive security certification that we have not established.'),
('Are all regions and features finished?', 'Main progression and regional contract content are authored, but the project checklist still identifies playthroughs, balancing, temporary vehicles and ending presentation work. We will distinguish implemented content from tested completion and demonstrate only scenarios rehearsed on the presentation build.'),
('What are the main limitations?', 'The structural model is simplified and planar; width is predefined; material properties are gamified; full 3D bending, shear and torsion analysis is outside the current solver. Wind, seismic and long-term deterioration scenarios are outside the reviewed implementation. Learning claims also depend on the evaluation evidence.'),
('What would you improve next?', 'Priorities are to reconcile evaluation results, complete device and end-to-end testing, balance late contracts, and improve instructional feedback. Larger extensions could include editable width, a richer structural model, additional load scenarios and stronger learning-outcome evaluation.'),
('What if the panel finds a mismatch or the demo fails?', 'We should acknowledge the specific issue, explain what the evidence currently supports, and identify the correction needed. If a live scenario fails, state what happened and use a clearly labeled prepared recording where appropriate. Do not present a recording as a successful live test.')])
]

qnum=0
for title,kicker,qa in qa_sections:
    items=[]
    for q,a in qa:
        qnum+=1
        items.extend([p(f'{qnum:02d}. {escape(q)}','q'),p(escape(a),'answer')])
    add(title,kicker,items)

add('Final rehearsal and paper checks','10 / CHECK BEFORE YOU MEMORIZE',[
 *box('<b>Priority:</b> The paper has changed since the first review. Several technical descriptions are now corrected, but the evaluation sections still contain contradictions. Verify the raw response sheet and final tables before quoting results.',True),
 sub('Paper details that need reconciliation'),
 p('<b>Evaluation categories:</b> Eight categories appear in the objectives/coding discussion, but seven differently named dimensions appear in the results.<br/><b>Conflicting values:</b> The accuracy narrative reports a mean of 4.45 while the ranking narrative reports 3.98. Other result summaries also differ.<br/><b>Standard deviations:</b> Values around 4 are reported for a five-point rating scale. Verify the spreadsheet ranges, formulas and column labels.<br/><b>Interpretation:</b> Check ratings against the stated bands; some narratives label values above the Strongly Agree threshold as Agree.<br/><b>Demographics and tense:</b> Computer-literacy percentages are reversed between passages. Planned-evaluation wording appears alongside completed-result claims.'),
 sub('Your final 60-second rehearsal'),
 p('"Civil Craft is a learning-support game for basic structural mechanics. Players inspect a load, build within contract limits, test a bridge and improve the design. Our custom 2D solver calculates axial member forces, and the stress percentage compares those forces with configured capacities. PhysX handles motion and collisions. Width is fixed because transverse behavior is outside the current model. Lessons, the Almanac and quizzes support concept review. Photon handles multiplayer networking, and PlayFab supports accounts and cloud records. We distinguish simplified gameplay behavior from validated engineering calculations and limit our educational conclusions to our verified evidence."'),
 sub('Presentation readiness'),
 p('Prepare an offline/tutorial save, a weak bridge and revised design, a later load-test save, two matching multiplayer builds, and populated website records. Check login, Wi-Fi, projection, font size and sound. Keep a labeled backup video. Have one verified results sheet and let one team member handle statistical questions.'),
 sub('How this guide was grounded'),
 p('Prepared 7 November 2026 from the current Google Docs paper and local project implementation. It is a rehearsal aid, not a certification of completed testing or an audit of the cited literature. Suggested answers about validation and improvements describe proposed work where explicitly stated.','small'),
 p('<b>Paper:</b> <link href="https://docs.google.com/document/d/1cfhx7wGRHunFUdWgUh51zdUPo2eWKq6Z3dCWMstWa-Y/edit" color="#087F80">Defense Paper - Google Docs</link><br/><b>Project evidence:</b> DeterministicBridgeStressSolver.cs; BridgePhysicsManager.cs; BridgeMaterialSO.cs; ChallengeCompetitionScoring.cs; FusionConnectionManager.cs; Docs/BeamBridgeTutorialV2.md; Docs/NPC-Progression-Checklist.md; Docs/GameWebsiteIntegration.md; Docs/LeaderboardContractVisibility.md; Backend/WEBSITE_SHOP_INTEGRATION.md.','small')
])

c=canvas.Canvas(str(OUT),pagesize=(W,H))
c.setTitle('Civil Craft: Bridge Edition | Pre-Oral Cram and Panel Q&A')
c.setAuthor('Civil Craft - Defense Preparation')
measurements=[]
for n,(title,kicker,items) in enumerate(pages,1):
    c.setFillColor(TEAL); c.rect(0,H-9,W,9,fill=1,stroke=0)
    c.setFillColor(MUTED); c.setFont('Arial-Bold',8.4); c.drawString(LEFT,H-35,'CIVIL CRAFT: BRIDGE EDITION')
    c.setFont('Arial',8); c.drawRightString(W-RIGHT,H-35,kicker)
    c.setFillColor(INK); c.setFont('Arial-Bold',21); c.drawString(LEFT,H-68,title)
    c.setStrokeColor(LINE); c.line(LEFT,H-82,W-RIGHT,H-82)
    y=TOP
    for item in items:
        sw,sh=item.wrap(CW,H)
        before=item.getSpaceBefore() if hasattr(item,'getSpaceBefore') else 0
        after=item.getSpaceAfter() if hasattr(item,'getSpaceAfter') else 0
        y-=before
        if y-sh < BOTTOM:
            raise RuntimeError(f'Page {n} overflow by {BOTTOM-(y-sh):.1f}pt at {getattr(item,"text",type(item).__name__)}')
        item.drawOn(c,LEFT,y-sh)
        y-=sh+after
    measurements.append({'page':n,'bottom_y':round(y,1),'title':title})
    c.setStrokeColor(LINE); c.line(LEFT,37,W-RIGHT,37)
    c.setFillColor(MUTED); c.setFont('Arial',8)
    c.drawString(LEFT,23,'PRE-ORAL STUDY GUIDE  |  7 NOVEMBER 2026')
    c.drawRightString(W-RIGHT,23,f'{n:02d} / {len(pages):02d}')
    c.showPage()
c.save()
reader=PdfReader(str(OUT))
assert len(reader.pages)==10
alltext='\n'.join(page.extract_text() for page in reader.pages)
for phrase in ['42.','60%','width','4.45','3.98','Photon','K u = F']:
    assert phrase in alltext,phrase
(TMP/'guide-layout.json').write_text(json.dumps(measurements,indent=2),encoding='utf-8')
(TMP/'guide-extracted.txt').write_text(alltext,encoding='utf-8')
pdf=pdfium.PdfDocument(str(OUT))
thumbs=[]
for i in range(len(pdf)):
    img=pdf[i].render(scale=1.2).to_pil().convert('RGB')
    img.save(TMP/f'guide-page-{i+1:02d}.png')
    thumb=img.copy();thumb.thumbnail((357,505))
    tile=Image.new('RGB',(381,538),'#DDE4E8');tile.paste(thumb,((381-thumb.width)//2,8))
    ImageDraw.Draw(tile).text((12,518),f'Page {i+1}',fill='#172B3A')
    thumbs.append(tile)
contact=Image.new('RGB',(381*2,538*5),'#DDE4E8')
for i,tile in enumerate(thumbs):contact.paste(tile,((i%2)*381,(i//2)*538))
contact.save(TMP/'guide-contact-sheet.png')
print(json.dumps({'output':str(OUT),'pages':len(reader.pages),'words':len(alltext.split()),'layout':measurements},indent=2))
