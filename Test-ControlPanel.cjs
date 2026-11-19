// Offline DOM tests: no browser, no input events, no network or USB activity.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {JSDOM}=require('jsdom');
const root=__dirname,html=fs.readFileSync(path.join(root,'control-panel/index.html'),'utf8');
const dom=new JSDOM(html,{url:'http://127.0.0.1:17653/',runScripts:'outside-only'}),win=dom.window;
const settings={Profiles:{},SavedStyles:{},SelectedStyle:'equalizer',SelectedPreset:''},requests=[];
let fakeError=false;
let gate=null;
let currentFrame=null,sceneFrame=null;
win.structuredClone=structuredClone;win.matchMedia=()=>({matches:false});win.requestAnimationFrame=()=>1;win.setInterval=()=>1;
win.setTimeout=()=>1;win.Lighting3D={create:()=>({update:frame=>sceneFrame=frame}),drawGraph:()=>{}};
win.fetch=async(url,options)=>{
  if(options?.method==='POST'){
    const body=JSON.parse(options.body);requests.push({url,body});
    if(gate){const wait=gate;gate=null;await wait;}
    if(fakeError)return {ok:false,json:async()=>({Error:'The selected keyboard is disconnected.'})};
    if(url.endsWith('/presets')){const id='preset-0123456789abcdef0123456789abcdef';const saved={Name:body.Name.trim(),Style:body.Style,Profile:structuredClone(body.Profile)};settings.SavedStyles[id]=saved;return {ok:true,json:async()=>({Id:id,SavedStyle:saved})};}
    if(url.endsWith('/save') || url.endsWith('/apply')){if(body.PresetId)settings.SavedStyles[body.PresetId].Profile=structuredClone(body.Profile);else settings.Profiles[body.Style]=structuredClone(body.Profile);settings.SelectedStyle=body.Style;settings.SelectedPreset=body.PresetId || '';}
    return {ok:true,json:async()=>({Ok:true})};
  }
  if(url.endsWith('/live'))return {ok:true,json:async()=>currentFrame};
  return {ok:true,json:async()=>({Settings:settings,Devices:{Hp:true,Legion:true,Razer:true},Running:false,Active:'',Live:null,Error:''})};
};
win.eval(fs.readFileSync(path.join(root,'control-panel/app.js'),'utf8')+'\nwindow.controlTest={choose,action,changed,renderControls,paint,poll,stepControl,togglePower,resetStyle,undoReset,saveNewStyle,setControlValue,setDraft:value=>Object.assign(draft,value),setState:value=>state=value};');
(async()=>{
  await new Promise(resolve=>setImmediate(resolve));
  const studio=win.document.body.dataset.layout==='studio';
  assert.equal(win.lightingPreview.keyCount(),studio?0:104);assert.equal(win.document.querySelectorAll('.style-card').length,4);assert.equal(win.document.querySelectorAll('#nativePatternSelect option').length,20);
  assert.equal(win.document.querySelector('#hpConnection').textContent,'Connected');
  assert.equal(requests.length,0,'Loading the panel must not change lighting');
  const test=win.controlTest;
  const checkStage=()=>{if(studio)for(const view of win.document.querySelectorAll('[data-view]'))for(let element=view;element;element=element.parentElement)assert.equal(element.hasAttribute('hidden'),false,'3D device views must stay visible');};
  const frame=(Style,HpKeys,HpBrightness=5,LegionColors=[[0,0,0],[0,0,0],[0,0,0],[0,0,0]])=>({Version:3,Style,Timestamp:new Date().toISOString(),Drive:1,HpKeys,HpBrightness,LegionColors,LegionBrightness:2,RazerNative:true,RazerRgb:null});
  const show=output=>test.setState({Running:!output.Style.startsWith('hp-'),Active:output.Style,Live:output});
  const litCount=()=>studio?sceneFrame.HpKeys.filter(value=>value===7).length:win.document.querySelectorAll('.key-group.lit').length;
  const choose=test.choose;test.choose=async id=>{const result=await choose(id);checkStage();return result;};
  await test.choose('dim-dip');assert.equal(requests.at(-1).url,'/api/apply');assert.equal(requests.at(-1).body.Style,'dim-dip');
  test.setDraft({HpBrightness:2});test.changed();test.renderControls();await test.action('apply');assert.equal(settings.Profiles['dim-dip'].HpBrightness,2);assert.equal(win.document.querySelector('#hpBrightnessValue').textContent,'40%');
  await test.choose('hp-1');test.setDraft({HpBrightness:1});test.changed();test.renderControls();await test.action('apply');
  await test.choose('hp-10');assert.equal(win.lightingPreview.getDraft().HpBrightness,5);
  await test.choose('hp-1');assert.equal(win.lightingPreview.getDraft().HpBrightness,1);assert.equal(requests.at(-1).body.Profile.HpBrightness,1);
  await test.choose('hp-2');test.setDraft({Rows:32,HpBrightness:5});test.renderControls();show(frame('hp-2',Array.from({length:125},(_,slot)=>slot%6===5?7:0)));test.paint(1000);
  assert.equal(litCount(),studio?20:13);if(!studio)for(const key of win.document.querySelectorAll('.key-group.lit'))assert.equal(key.dataset.row,'5');
  await test.choose('equalizer');const live={Running:true,Live:frame('equalizer',Array(125).fill(0))};test.setState(live);test.paint(2000);assert.equal(litCount(),0);
  live.Live.HpKeys=Array.from({length:125},(_,slot)=>slot%6===5 || Math.floor(slot/6)===5?7:0);test.paint(3000);if(!studio)assert.equal(win.document.querySelectorAll('.key-group.lit[data-row=\"5\"]').length,13);assert.ok(litCount()>13);
  await test.action('apply');assert.equal(requests.at(-1).body.Style,'equalizer');assert.equal(requests.at(-1).body.Profile.FallMilliseconds,220);
  fakeError=true;assert.equal(await test.action('apply'),false);assert.match(win.document.querySelector('#message').textContent,/disconnected/);assert.equal(win.document.querySelector('#resetButton').disabled,false);fakeError=false;
  // Slow hardware response: newer selections stay queued, and only the last pending one runs.
  let release;gate=new Promise(resolve=>release=resolve);const start=requests.length;
  const first=test.choose('dim-dip'),superseded=test.choose('soft'),last=test.choose('hp-10');
  assert.equal(await superseded,false);assert.equal(requests.length,start+1);assert.equal(win.lightingPreview.getSelected(),'hp-10');
  release();await first;assert.equal(await last,true);
  assert.deepEqual(requests.slice(start).map(request=>request.body.Style),['dim-dip','hp-10']);
  assert.equal(settings.SelectedStyle,'hp-10');assert.match(win.document.querySelector('#message').textContent,/Typing reactive applied/);
  // Stop must cancel pending style writes so an earlier selection cannot restart music.
  gate=new Promise(resolve=>release=resolve);const stoppingStart=requests.length;
  const current=test.choose('equalizer'),cancelled=test.choose('soft'),stopped=test.action('stop');
  assert.equal(await cancelled,false);release();await current;await stopped;
  assert.deepEqual(requests.slice(stoppingStart).map(request=>request.url),['/api/apply','/api/stop']);
  assert.equal(win.document.querySelector('#applyButton'),null);assert.equal(win.document.querySelector('#saveButton'),null);
  await test.choose('equalizer');test.setDraft({HpBrightness:2,FallMilliseconds:410,Width:1});test.changed();await test.action('apply');
  await test.resetStyle();assert.equal(win.lightingPreview.getDraft().HpBrightness,5);assert.equal(win.lightingPreview.getDraft().FallMilliseconds,220);
  assert.equal(win.document.querySelector('#undoButton').hidden,false,'Reset must expose Undo');
  assert.equal(await test.undoReset(),true);assert.equal(win.lightingPreview.getDraft().HpBrightness,2);assert.equal(win.lightingPreview.getDraft().FallMilliseconds,410);
  assert.equal(win.document.querySelector('#undoButton').hidden,true,'Undo must consume its reset snapshot');
  const savedId=await test.saveNewStyle('My deep spectrum');assert.equal(win.lightingPreview.getSelected(),savedId);assert.equal(requests.at(-1).body.Style,'equalizer');assert.equal(requests.at(-1).body.PresetId,savedId);
  test.setDraft({HpBrightness:1});test.changed();await test.action('apply');assert.equal(settings.SavedStyles[savedId].Profile.HpBrightness,1);assert.equal(settings.Profiles.equalizer.HpBrightness,2);
  await test.choose('equalizer');assert.equal(win.lightingPreview.getDraft().HpBrightness,2);
  await test.choose(savedId);assert.equal(win.lightingPreview.getDraft().HpBrightness,1);assert.equal(win.document.querySelector('#previewTitle').textContent,'My deep spectrum');
  test.setState({Running:true,Live:frame('equalizer',Array(125).fill(0))});test.paint(4000);assert.equal(litCount(),0,'Custom equalizers must retain live telemetry');
  await test.resetStyle();await test.undoReset();assert.equal(win.lightingPreview.getDraft().HpBrightness,1,'Undo must restore the custom style snapshot');
  await test.resetStyle();test.setDraft({FallMilliseconds:330});test.changed();assert.equal(win.document.querySelector('#undoButton').hidden,true,'A later adjustment must clear the old reset snapshot');
  if(studio){
    test.setDraft({LegionBrightness:1});test.renderControls();assert.equal(win.document.querySelector('#legionBrightnessValue').textContent,'50%');
    for(const [now,drive] of [[5000,0],[6000,1]]){const output=frame('equalizer',Array(125).fill(drive?7:0));output.Drive=drive;show(output);test.paint(now);checkStage();assert.equal(sceneFrame,output,'3D must receive the exact outgoing frame');assert.equal(win.document.querySelector('#previewSource').textContent,'Live lighting output');}
    assert.equal(win.document.querySelector('#mouseOwner').textContent,'Native Chroma');
    await test.choose('dim-dip');test.paint(7000);assert.equal(win.document.querySelector('#mouseOwner').textContent,'Music Lighting sync');
    assert.equal(win.document.querySelectorAll('[data-graph]').length,win.document.querySelector('#spread')?4:3);assert.equal(win.document.querySelectorAll('.brightness-dial').length,2);assert.equal(win.document.querySelector('svg'),null,'Studio must not contain hardware SVG drawings');
  }
  await test.choose('equalizer');test.setControlValue('legionBrightness',0);assert.equal(win.lightingPreview.getDraft().LegionBrightness,0);await test.action('apply');assert.equal(requests.at(-1).body.Profile.LegionBrightness,0);
  test.setControlValue('sensitivity',147);assert.equal(win.lightingPreview.getDraft().Sensitivity,1.45);test.setControlValue('fall',999);assert.equal(win.lightingPreview.getDraft().FallMilliseconds,700);test.setControlValue('hold',-1);assert.equal(win.lightingPreview.getDraft().HoldMilliseconds,0);test.setControlValue('legionEffect',2);assert.equal(win.lightingPreview.getDraft().LegionEffect,2,'Classic dropdown must survive the shared visual-control adapter');
  if(win.document.querySelector('#spread')){test.setControlValue('spread',.8);assert.equal(win.lightingPreview.getDraft().Width,1);assert.equal(win.document.querySelector('#spreadValue').textContent,'Wide');assert.equal(win.document.querySelector('[data-graph=spread]').getAttribute('aria-valuetext'),'Wide');test.setControlValue('spread',99);assert.equal(win.lightingPreview.getDraft().Width,2);await test.action('apply');assert.equal(requests.at(-1).body.Profile.Width,2,'The range graph must apply and save the actual hardware spread setting');test.setControlValue('spread',-4);assert.equal(win.lightingPreview.getDraft().Width,0);}
  const stale=frame('equalizer',Array(125).fill(7));stale.Timestamp=new Date(Date.now()-2000).toISOString();show(stale);test.paint(8000);if(studio)assert.equal(sceneFrame,null);else assert.equal(litCount(),0);assert.equal(win.document.querySelector('#previewSource').textContent,'Connecting to lighting output');
  await test.setControlValue('hpBrightness',3,true);await test.togglePower('hpBrightness');assert.equal(requests.at(-1).body.Profile.HpBrightness,0);await test.togglePower('hpBrightness');assert.equal(requests.at(-1).body.Profile.HpBrightness,3,'Power restores the previous brightness');await test.stepControl('hpBrightness',1);assert.equal(requests.at(-1).body.Profile.HpBrightness,4);
  await new Promise(resolve=>setImmediate(resolve));
  settings.SelectedStyle='hp-1';settings.SelectedPreset='';settings.Profiles['hp-1']={...win.lightingPreview.getDraft(),HpBrightness:2};await test.poll();assert.equal(win.lightingPreview.getSelected(),'hp-1','Another panel selection must update this panel');assert.equal(win.lightingPreview.getDraft().HpBrightness,2);
  test.setControlValue('hpBrightness',1);settings.Profiles['hp-1'].HpBrightness=5;await test.poll();assert.equal(win.lightingPreview.getDraft().HpBrightness,1,'A remote poll must not overwrite an active edit');await test.action('apply');
  fakeError=true;await test.action('apply');await new Promise(resolve=>setImmediate(resolve));assert.equal(win.document.body.dataset.feedback,'error');assert.equal(win.document.querySelector('#applyStatus').textContent,'Change could not apply');fakeError=false;await test.action('apply');await new Promise(resolve=>setImmediate(resolve));assert.equal(win.document.body.dataset.feedback,'applied');
  const report='PASS automatic selection and queue, reset/undo, named copies, power restore, +/- controls, cross-panel synchronization and draft protection, error feedback, brightness zero, exact outgoing frames, native ownership and stale-frame blackout. No input events, browser, network or USB commands.';
  await new Promise(resolve=>setImmediate(resolve));
  fs.writeFileSync(path.join(root,'control-panel-dom-test.txt'),report);console.log(report);dom.window.close();
})().catch(error=>{console.error(error);dom.window.close();process.exitCode=1;});
