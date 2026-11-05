from pathlib import Path
import os, subprocess
repo=Path(__file__).resolve().parents[2]
root=repo/'deploy-artifacts/thermal-render-checks';root.mkdir(parents=True,exist_ok=True)
edge=Path(os.environ.get('EDGE_BIN', str(Path(os.environ.get('ProgramFiles(x86)', 'C:/Program Files (x86)'))/'Microsoft/Edge/Application/msedge.exe')))
css='''@page{size:A4;margin:0}html,body{margin:0;background:white;color:black;font:14px Tahoma} .receipt{width:72mm;box-sizing:border-box;border:1px solid black;direction:rtl;text-align:right;margin:0 auto;padding:1mm}.receipt h1{text-align:center;font-size:22px}.receipt p{line-height:2;border-bottom:1px dashed black}@media print{body{width:80mm;margin:0}.receipt{width:72mm;margin:0 auto}}'''
receipt='''<div class="receipt" data-print-ready="true"><h1>پاپی PAPEY</h1><p>رسید آزمایشی چاپ — سفارش واقعی نیست</p><p>تیشرت / مشکی / XL / تعداد ۲</p><p>مبلغ نهایی: ۲٬۰۰۰٬۰۰۰ تومان</p><p>مرجع پرداخت: TEST-PRINT</p><p>سپاس از خرید شما</p></div>'''
for name in ['rtl-driver-before','rtl-driver-after']:
 script=(repo/'wwwroot/js/thermal-print.js').read_text(encoding='utf-8') if name.endswith('after') else ''
 html='<html lang="fa" dir="rtl"><meta charset="utf-8"><style>'+css+'</style><body>'+receipt+'<script>'+script+'</script></body></html>'
 fixture=root/(name+'.html');fixture.write_text(html,encoding='utf-8');pdf=root/(name+'.pdf')
 command=[str(edge),'--headless','--disable-gpu','--no-pdf-header-footer','--user-data-dir='+str(root/(name+'-profile')),'--print-to-pdf='+str(pdf),'--virtual-time-budget=1500',fixture.as_uri()]
 done=subprocess.run(command,capture_output=True,timeout=50,creationflags=subprocess.CREATE_NO_WINDOW)
 import time
 for attempt in range(40):
  if pdf.exists():break
  time.sleep(.25)
 assert pdf.exists(),done.stderr.decode(errors='replace')[-500:]
 subprocess.run(['pdftoppm','-png','-r','96','-singlefile',str(pdf),str(root/name)],check=True,capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
print('Rendered before/after thermal layout fixtures:',root)
from PIL import Image,ImageChops
for name in ['rtl-driver-before','rtl-driver-after']:
 im=Image.open(root/(name+'.png')).convert('RGB');ink=ImageChops.difference(im,Image.new('RGB',im.size,'white'));box=ink.getbbox()
 print(name,'ink bounds',box,'80mm roll ends at x=303px')
 if name.endswith('before'):assert box[0]>303
 else:assert box[0]>=0 and box[2]<=303
print('PASS: With a driver-sized page, RTL placement used to lie entirely beyond an 80mm roll; corrected content is entirely inside its printable width.')
