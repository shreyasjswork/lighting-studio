'use strict';
const $ = id => document.getElementById(id);
const studio = document.body.dataset.layout==='studio';
const defaults = () => ({HpBrightness:5,HpSpeed:3,LegionBrightness:2,LegionEffect:4,LegionColors:['#93f553','#44dac3','#468ded','#b07bef'],Sensitivity:1,HoldMilliseconds:55,FallMilliseconds:220,Width:0,BottomSupport:true,Rows:63,Name:''});
const styles = [
  {id:'dim-dip',name:'Dim / Dip',description:'Your favourite. Strong beats, changing colours.',bars:[5,9,23,33,23,9,5]},
  {id:'soft',name:'Soft Pulse',description:'Gentle, flowing light across all three.',bars:[12,17,22,26,22,17,12]},
  {id:'equalizer',name:'Spectrum Equalizer',description:'Bright bars, deep gaps, clear musical peaks.',bars:[14,26,9,33,21,7,17]},
  {id:'razer-native',name:'Original Razer',description:'Native mouse visualizer, pulsing keyboards.',bars:[5,28,19,33,9,22,13]}
];
const nativeNames = {1:'Steady',2:'Custom rows',10:'Typing reactive'};
let settings={Profiles:{},SavedStyles:{},SelectedStyle:'equalizer',SelectedPreset:''},selected='equalizer',draft=defaults(),state={},dirty=false,busy=false,focusedDevice='hp',firstLoad=true,lastAudio='equalizer',lastNative='hp-1';
let currentAction=null,polling=false,appliedStyle='',appliedProfile=null;
let lastReset=null,savingPreset=false,editRevision=0,feedbackError=false;
const powerMemory=new Map();
const actionQueue=[];
const keys=[],ns='http://www.w3.org/2000/svg',rowColours=['#ef6b71','#ffb64d','#ffe064','#83ed6b','#4de1dc','#7799ff'];
const isPreset=id=>!!settings.SavedStyles?.[id];
const effectId=(id=selected)=>settings.SavedStyles?.[id]?.Style || id;
const profileFor=id=>isPreset(id)?settings.SavedStyles[id].Profile:settings.Profiles[id];
function storeDraft(id,profile) {if(isPreset(id))settings.SavedStyles[id].Profile=structuredClone(profile);else settings.Profiles[id]=structuredClone(profile);}
function styleName(id) {return settings.SavedStyles?.[id]?.Name || settings.Profiles?.[id]?.Name || styles.find(s=>s.id===id)?.name || nativeNames[Number(id?.slice(3))] || `Pattern ${id?.slice(3)}`;}
function renderPresets() {
  const list=$('presetList');if(!list)return;list.replaceChildren();
  for(const [id,preset] of Object.entries(settings.SavedStyles || {})) {
    const button=document.createElement('button');button.className='preset-choice';button.dataset.style=id;button.textContent=preset.Name;button.title=`${styleName(preset.Style)} · saved settings`;button.setAttribute('aria-pressed',String(id===selected));button.addEventListener('click',()=>choose(id));list.append(button);
  }
  $('savedPresets').hidden=!list.childElementCount;
}
function svgNode(tag,attributes,parent) {const node=document.createElementNS(ns,tag);for(const [key,value] of Object.entries(attributes))node.setAttribute(key,value);parent?.append(node);return node;}
function keyboard() {
  if(studio)return;
  const svg=$('keyboardPreview');svgNode('rect',{x:1,y:1,width:1028,height:343,rx:14,fill:'#111610',stroke:'#39483a','stroke-width':2},svg);
  svgNode('path',{d:'M13 331H1017',stroke:'#93f553','stroke-width':1.2,opacity:.27},svg);
  const unit=42,gap=5,startX=19,startY=23,pitch=50;
  function key(label,row,u,width=1,height=1) {
    const x=startX+u*unit,y=startY+row*pitch,w=width*unit-gap,h=height*pitch-gap;
    const g=svgNode('g',{class:'key-group','data-row':row,style:`--key-color:${rowColours[row]}`},svg);
    svgNode('rect',{class:'key-body',x,y,width:w,height:h,rx:4},g);const text=svgNode('text',{class:'key-text',x:x+w/2,y:y+h/2},g);text.textContent=label;
    keys.push({g,row,column:Math.min(20,Math.floor((u+width/2)/23.65*21))});
  }
  key('esc',0,0);for(let i=1;i<=12;i++)key(`F${i}`,0,1.7+(i-1)*1.05+(i>4?.3:0)+(i>8?.3:0));['prt','scr','pause'].forEach((k,i)=>key(k,0,15.7+i));
  ['~','1','2','3','4','5','6','7','8','9','0','−','='].forEach((k,i)=>key(k,1,i));key('backspace',1,13,2);['ins','home','pg up'].forEach((k,i)=>key(k,1,15.7+i));['num','/','*','−'].forEach((k,i)=>key(k,1,19.4+i));
  key('tab',2,0,1.5);['Q','W','E','R','T','Y','U','I','O','P','[',']'].forEach((k,i)=>key(k,2,1.5+i));key('\\',2,13.5,1.5);['del','end','pg dn'].forEach((k,i)=>key(k,2,15.7+i));['7','8','9'].forEach((k,i)=>key(k,2,19.4+i));key('+',2,22.4,1,2);
  key('caps',3,0,1.8);['A','S','D','F','G','H','J','K','L',';',"'"].forEach((k,i)=>key(k,3,1.8+i));key('enter',3,12.8,2.2);['4','5','6'].forEach((k,i)=>key(k,3,19.4+i));
  key('shift',4,0,2.3);['Z','X','C','V','B','N','M',',','.','/'].forEach((k,i)=>key(k,4,2.3+i));key('shift',4,12.3,2.7);key('↑',4,16.7);['1','2','3'].forEach((k,i)=>key(k,4,19.4+i));key('enter',4,22.4,1,2);
  key('ctrl',5,0,1.3);key('win',5,1.3,1.3);key('alt',5,2.6,1.3);key('space',5,3.9,6.2);key('alt',5,10.1,1.2);key('win',5,11.3,1.2);key('menu',5,12.5,1.2);key('ctrl',5,13.7,1.3);['←','↓','→'].forEach((k,i)=>key(k,5,15.7+i));key('0',5,19.4,2);key('.',5,21.4);
}
function buildChoices() {
  $('styleGrid').replaceChildren();
  for(const style of styles) {
    const button=document.createElement('button');button.className='style-card';button.dataset.style=style.id;button.setAttribute('aria-pressed','false');
    const bars=document.createElement('div');bars.className='mini-bars';bars.setAttribute('aria-hidden','true');for(const height of style.bars){const bar=document.createElement('i');bar.style.height=`${height}px`;bars.append(bar);}
    const name=document.createElement('strong');name.textContent=style.name;const description=document.createElement('span');description.textContent=style.description;button.append(bars,name,description);button.addEventListener('click',()=>choose(style.id));$('styleGrid').append(button);
  }
  for(const [pattern,note] of [[1,'All keys, steady light'],[10,'Responds to your typing'],[2,'Choose your own rows']]) {
    const button=document.createElement('button');button.dataset.style=`hp-${pattern}`;const label=document.createElement('span');label.textContent=nativeNames[pattern];const small=document.createElement('small');small.textContent=note;button.append(label,small);button.addEventListener('click',()=>choose(`hp-${pattern}`));$('nativeChoices').append(button);
  }
  for(let pattern=1;pattern<=20;pattern++){const option=document.createElement('option');option.value=`hp-${pattern}`;option.textContent=nativeNames[pattern] || `Pattern ${pattern}`;$('nativePatternSelect').append(option);}
  ['Function','Numbers','QWERTY','ASDF','ZXCV','Bottom'].forEach((name,row)=>{const button=document.createElement('button');button.textContent=name;button.dataset.row=row;button.addEventListener('click',()=>{draft.Rows^=1<<row;changed();renderControls();action('apply');});$('rowButtons').append(button);});
}
function choose(id) {
  if(firstLoad || (!isPreset(id) && !/^(dim-dip|soft|equalizer|razer-native|hp-(?:[1-9]|1[0-9]|20))$/.test(id)))return Promise.resolve(false);
  editRevision++;feedbackError=false;storeDraft(selected,draft);selected=id;draft={...defaults(),...structuredClone(profileFor(id) || {})};dirty=false;
  if(effectId(id).startsWith('hp-'))lastNative=id;else lastAudio=id;
  renderControls();
  return action('apply');
}
function controlStatus() {
  const queued=actionQueue.some(job=>job.name==='apply' && job.style===selected);
  const applying=currentAction?.name==='apply' && currentAction.style===selected;
  const active=appliedStyle===selected && JSON.stringify(appliedProfile)===JSON.stringify(draft);
  document.body.dataset.feedback=feedbackError?'error':queued || applying?'applying':dirty?'adjusting':active?'applied':'selected';
  $('applyStatus').textContent=feedbackError?'Change could not apply':queued?'Next style queued':applying?'Applying your style…':dirty?'Adjusting your style':active?'Applied and saved':'Select a style to start';
  $('savedIndicator').textContent=dirty?'Previewing adjustment':'Brightness remembered per style';
  $('applyDescription').textContent=studio?'Auto saved':'Styles apply on selection. Adjustments apply when you release the control.';
  $('applyDot').classList.toggle('pending',queued || applying || dirty);
  if($('undoButton')){$('undoButton').hidden=!lastReset || lastReset.style!==selected;$('undoButton').disabled=busy;}
  if($('saveAsButton'))$('saveAsButton').disabled=busy || savingPreset || firstLoad;
}
function changed(keepUndo=false) {editRevision++;feedbackError=false;if(!keepUndo && lastReset?.style===selected)lastReset=null;dirty=true;controlStatus();}
async function resetStyle() {
  if(busy)return false;lastReset={style:selected,profile:structuredClone(draft)};
  draft={...defaults(),Name:draft.Name};changed(true);renderControls();return action('apply');
}
async function undoReset() {
  if(busy || !lastReset || lastReset.style!==selected)return false;
  draft=structuredClone(lastReset.profile);lastReset=null;changed(true);renderControls();return action('apply');
}
async function saveNewStyle(name) {
  if(busy || savingPreset)return null;
  const source=selected,profile=structuredClone(draft),base=effectId(source);
  savingPreset=true;controlStatus();
  try {
    const result=await request('presets',{Name:name,Style:base,Profile:{...profile,Name:''}});
    settings.SavedStyles ||= {};settings.SavedStyles[result.Id]=result.SavedStyle;renderPresets();
    if(source===selected && JSON.stringify(profile)===JSON.stringify(draft))await choose(result.Id);
    else message(`${result.SavedStyle.Name} saved.`);
    return result.Id;
  }finally{savingPreset=false;controlStatus();}
}
function renderControls() {
  const effect=effectId(),native=effect.startsWith('hp-'),equalizer=effect==='equalizer';
  $('audioTab').setAttribute('aria-selected',String(!native));$('nativeTab').setAttribute('aria-selected',String(native));$('audioStyles').hidden=native;$('nativeStyles').hidden=!native;
  $('nativeControls').hidden=!native;$('rowControls').hidden=effect!=='hp-2';$('equalizerControls').hidden=!equalizer;$('legionNative').hidden=!native;
  if($('hpEqualizerControls'))$('hpEqualizerControls').hidden=!equalizer;
  $('hpBrightness').value=draft.HpBrightness;$('hpBrightnessValue').textContent=`${draft.HpBrightness*20}%`;
  $('hpSpeed').value=draft.HpSpeed;$('hpSpeedValue').textContent=`${draft.HpSpeed} / 5`;
  $('sensitivity').value=Math.round(draft.Sensitivity*100);$('sensitivityValue').textContent=`${Math.round(draft.Sensitivity*100)}%`;
  $('fall').value=draft.FallMilliseconds;$('fallValue').textContent=`${draft.FallMilliseconds} ms`;
  $('hold').value=draft.HoldMilliseconds;$('holdValue').textContent=`${draft.HoldMilliseconds} ms`;
  if($('spread')){$('spread').value=draft.Width;$('spreadValue').textContent=['Focused','Wide','Full'][draft.Width];}
  $('bottomSupport').checked=draft.BottomSupport;$('patternName').value=draft.Name;
  $('legionBrightness').value=draft.LegionBrightness;$('legionEffect').value=draft.LegionEffect;
  if($('legionBrightnessValue'))$('legionBrightnessValue').textContent=`${draft.LegionBrightness*50}%`;
  for(const dial of document.querySelectorAll('.brightness-dial')){const input=$(dial.dataset.control),level=Number(input.value);dial.style.setProperty('--dial-level',String(level/Number(input.max)));dial.setAttribute('aria-valuenow',String(level));dial.setAttribute('aria-valuetext',`${Math.round(level/Number(input.max)*100)} percent`);}
  for(const button of document.querySelectorAll('[data-power-for]')){const on=Number($(button.dataset.powerFor).value)>0;button.setAttribute('aria-pressed',String(on));button.setAttribute('aria-label',`${button.dataset.powerFor==='hpBrightness'?'HP':'Legion'} lighting ${on?'on; turn off':'off; turn on'}`);}
  [...$('zoneColors').querySelectorAll('input')].forEach((input,i)=>{input.value=draft.LegionColors[i];input.disabled=draft.LegionEffect!==1;});
  document.querySelectorAll('[data-width]').forEach(button=>{button.classList.toggle('selected',Number(button.dataset.width)===draft.Width);button.setAttribute('aria-pressed',String(Number(button.dataset.width)===draft.Width));});
  document.querySelectorAll('[data-row]').forEach(button=>{if(button.tagName==='BUTTON'){const on=!!(draft.Rows & 1<<Number(button.dataset.row));button.classList.toggle('selected',on);button.setAttribute('aria-pressed',String(on));}});
  document.querySelectorAll('[data-style]').forEach(button=>{button.classList.toggle('selected',button.dataset.style===selected);button.setAttribute('aria-pressed',String(button.dataset.style===selected));});
  $('nativePatternSelect').value=native?effect:effectId(lastNative);
  for(const graph of document.querySelectorAll('[data-editor]')){graph.setAttribute('aria-valuenow',String($(graph.dataset.control).value));graph.setAttribute('aria-valuetext',graph.dataset.graph==='spread'?['Focused','Wide','Full'][draft.Width]:graph.dataset.graph==='gain'?`${draft.Sensitivity.toFixed(2)} times amplitude`:`${$(graph.dataset.control).value} milliseconds`);}
  for(const button of document.querySelectorAll('[data-value-for]'))button.setAttribute('aria-pressed',String(Number($(button.dataset.valueFor).value)===Number(button.dataset.value)));
  for(const option of $('nativePatternSelect').options)option.textContent=styleName(option.value);
  $('previewTitle').textContent=draft.Name || styleName(selected);controlStatus();
  $('stageDetail').textContent=equalizer?'21 bands · 6 rows':native?'Built-in keyboard lighting':'Playback audio';
  const nativeMouse=native || effect==='equalizer' || effect==='razer-native';$('deviceNote').querySelector('strong').textContent=nativeMouse?'The mouse keeps its original Razer Visualizer.':'All three devices follow this audio style.';
  $('deviceNote').querySelector('p').textContent=nativeMouse?'Enable Audio Visualizer in the Razer Chroma app. Its mouse effect remains independent of the keyboard settings.':'The HP, Legion and Basilisk share the same music pulse. Switching back to Spectrum releases the mouse to Razer.';
  $('mouseOwner').textContent=studio?(nativeMouse?'Native Chroma':'Music Lighting sync'):nativeMouse?"Razer's app controls the mouse. This illustration doesn't read its LEDs.":'The mouse follows the same pulse as the keyboards.';
  $('mouseOwner').title=nativeMouse?'Razer Chroma controls the mouse LEDs. Its live LED data is unavailable here. Enable Audio Visualizer in the Razer app.':'The mouse follows the shared music pulse.';
  if($('razerHint'))$('razerHint').textContent=nativeMouse?'Native Chroma output isn’t available to this preview.':'Live colour follows the outgoing mouse command.';
}
async function request(path,body) {
  const response=await fetch(`/api/${path}`,body===undefined?{cache:'no-store'}:{method:'POST',headers:{'Content-Type':'application/json','X-Lighting-Token':document.querySelector('meta[name=lighting-token]').content},body:JSON.stringify(body)});
  const result=await response.json();if(!response.ok)throw new Error(result.Error || 'The lighting change did not complete.');return result;
}
function message(text,error=false){$('message').textContent=text;$('message').classList.toggle('error',error);}
function action(name) {
  const style=selected,profile=structuredClone(draft),effect=effectId(style),presetId=isPreset(style)?style:'';
  if(name==='apply' || name==='save')storeDraft(style,profile);
  return new Promise(resolve=>{
    // Keep the latest pending selection. Never cancel a write already in progress.
    if(name==='apply' || name==='stop')for(let i=actionQueue.length-1;i>=0;i--)if(actionQueue[i].name==='apply'){actionQueue.splice(i,1)[0].resolve(false);}
    actionQueue.push({name,style,effect,presetId,profile,resolve});controlStatus();runActions();
  });
}
async function runActions() {
  if(busy)return;busy=true;$('resetButton').disabled=true;
  try {
    while(actionQueue.length) {
      const job=actionQueue.shift();currentAction=job;controlStatus();
      message(job.name==='apply'?`Applying ${job.profile.Name || styleName(job.style)}…`:job.name==='save'?'Saving settings…':'Stopping music sync…');
      try {
        await request(job.name,(job.name==='apply' || job.name==='save')?{Style:job.effect,Profile:job.profile,PresetId:job.presetId}:{});
        feedbackError=false;
        if(job.name==='apply'){appliedStyle=job.style;appliedProfile=structuredClone(job.profile);}
        if(job.name==='stop'){appliedStyle='';appliedProfile=null;}
        if((job.name==='apply' || job.name==='save') && selected===job.style && JSON.stringify(draft)===JSON.stringify(job.profile))dirty=false;
        renderControls();
        if(!actionQueue.length)message(job.name==='apply'?`${job.profile.Name || styleName(job.style)} applied and saved.`:job.name==='save'?'Pattern name saved.':'Music sync stopped.');
        job.resolve(true);
      }catch(error){feedbackError=true;appliedStyle='';appliedProfile=null;message(error.message,true);job.resolve(false);}
    }
  }finally{currentAction=null;busy=false;$('resetButton').disabled=false;controlStatus();await poll();}
}
async function poll() {
  if(polling)return;polling=true;const revision=editRevision;
  try {
    const prior=state.Live;state=await request('state');if(prior && Date.parse(prior.Timestamp)>Date.parse(state.Live?.Timestamp || 0))state.Live=prior;
    if(firstLoad || (!dirty && !busy && revision===editRevision && !feedbackError && !state.Error)){const oldSelection=selected,oldDraft=JSON.stringify(draft);settings=structuredClone(state.Settings);settings.SavedStyles ||= {};selected=settings.SelectedPreset || settings.SelectedStyle || 'equalizer';draft={...defaults(),...structuredClone(profileFor(selected) || {})};if(oldSelection!==selected || oldDraft!==JSON.stringify(draft))lastReset=null;if(effectId().startsWith('hp-'))lastNative=selected;else lastAudio=selected;firstLoad=false;renderPresets();renderControls();}
    for(const [id,key] of [['hpConnection','Hp'],['legionConnection','Legion'],['razerConnection','Razer']]){const connected=state.Devices?.[key];$(id).textContent=connected?'Connected':'Disconnected';$(id).classList.toggle('connected',!!connected);}
    let active=state.Running?state.Live?.Style || state.Active:state.Active;
    const savedActive=state.Settings?.SelectedPreset;if(savedActive && state.Settings.SavedStyles?.[savedActive]?.Style===active)active=savedActive;
    if(!dirty && !busy && !feedbackError && !state.Error && active===selected){appliedStyle=selected;appliedProfile=structuredClone(draft);controlStatus();}
    $('runningText').textContent=state.Running?'Music sync running':active?'Built-in lighting':'Music sync stopped';$('runningDot').classList.toggle('live',!!state.Running || !!active);$('activeName').textContent=active?styleName(active):'Preview';
  }catch(error){$('runningText').textContent='Control panel offline';$('runningDot').classList.remove('live');message('The local control panel is offline. Open Music Lighting from your desktop again.',true);}finally{polling=false;}
}
const controlFields=[['hpBrightness','HpBrightness',1],['hpSpeed','HpSpeed',1],['sensitivity','Sensitivity',.01],['fall','FallMilliseconds',1],['hold','HoldMilliseconds',1],['legionBrightness','LegionBrightness',1],['legionEffect','LegionEffect',1],...($('spread')?[['spread','Width',1]]:[])];
function setControlValue(id,value,apply=false){const entry=controlFields.find(field=>field[0]===id);if(!entry || !Number.isFinite(Number(value)))return false;const input=$(id),minimum=Number(input.min || (id==='legionEffect'?1:0)),maximum=Number(input.max || (id==='legionBrightness'?2:id==='legionEffect'?4:10000)),step=Number(input.step)||1;let numeric=Math.max(minimum,Math.min(maximum,Number(value)));numeric=minimum+Math.round((numeric-minimum)/step)*step;input.value=numeric;draft[entry[1]]=numeric*entry[2];changed();renderControls();if(apply)return action('apply');return true;}
function stepControl(id,step){if(firstLoad)return false;return setControlValue(id,Number($(id).value)+Number(step),true);}
function togglePower(id){if(firstLoad)return false;const input=$(id),level=Number(input.value),key=selected+':'+id;if(level>0)powerMemory.set(key,level);return setControlValue(id,level>0?0:(powerMemory.get(key)||Number(input.max)),true);}
function connectControls() {
  document.querySelectorAll('[data-value-for]').forEach(button=>button.addEventListener('click',()=>{if(!firstLoad)setControlValue(button.dataset.valueFor,Number(button.dataset.value),true);}));
  document.querySelectorAll('[data-step-for]').forEach(button=>button.addEventListener('click',()=>stepControl(button.dataset.stepFor,button.dataset.step)));
  document.querySelectorAll('[data-power-for]').forEach(button=>button.addEventListener('click',()=>togglePower(button.dataset.powerFor)));
  for(const [id] of controlFields) {
    const update=()=>{const input=$(id);if(input.value==='' || !input.checkValidity())return false;return setControlValue(id,Number(input.value));};
    $(id).addEventListener('input',update);$(id).addEventListener('change',()=>{if(update())action('apply');else message('Enter a value within the displayed range.',true);});
  }
  $('bottomSupport').addEventListener('change',()=>{draft.BottomSupport=$('bottomSupport').checked;changed();action('apply');});
  $('patternName').addEventListener('input',()=>{draft.Name=$('patternName').value;changed();$('previewTitle').textContent=draft.Name || styleName(selected);});
  $('patternName').addEventListener('change',()=>action('save'));
  $('widthButtons').addEventListener('click',event=>{const button=event.target.closest('[data-width]');if(button && !firstLoad){draft.Width=Number(button.dataset.width);changed();renderControls();action('apply');}});
  [...$('zoneColors').querySelectorAll('input')].forEach((input,i)=>{input.addEventListener('input',()=>{draft.LegionColors[i]=input.value;changed();});input.addEventListener('change',()=>action('apply'));});
  $('nativePatternSelect').addEventListener('change',()=>choose($('nativePatternSelect').value));$('audioTab').addEventListener('click',()=>choose(lastAudio));$('nativeTab').addEventListener('click',()=>choose(lastNative));
  $('stopButton').addEventListener('click',()=>action('stop'));
  $('resetButton').addEventListener('click',resetStyle);
  $('undoButton')?.addEventListener('click',undoReset);
  $('saveAsButton')?.addEventListener('click',()=>{
    const dialog=$('savePresetDialog');$('presetName').value=`${styleName(selected)} copy`.slice(0,40);$('presetError').textContent='';dialog.showModal();$('presetName').select();
  });
  $('cancelPreset')?.addEventListener('click',()=>$('savePresetDialog').close());
  $('savePresetForm')?.addEventListener('submit',async event=>{event.preventDefault();$('confirmPreset').disabled=true;try{const id=await saveNewStyle($('presetName').value);if(id)$('savePresetDialog').close();}catch(error){$('presetError').textContent=error.message;}finally{$('confirmPreset').disabled=false;}});
  $('refreshDevices').addEventListener('click',async()=>{try{await request('refresh',{});await poll();message('Device connections refreshed.');}catch(error){message(error.message,true);}});
  document.querySelectorAll('[data-device]').forEach(button=>button.addEventListener('click',()=>{focusedDevice=button.dataset.device;document.querySelectorAll('[data-device]').forEach(b=>b.classList.toggle('active',b===button));if(!studio){$('keyboardPreview').parentElement.hidden=focusedDevice!=='hp';$('zonePreview').hidden=focusedDevice!=='legion';$('mousePreview').hidden=focusedDevice!=='razer';$('previewDevice').textContent=focusedDevice==='hp'?'HP GK320':focusedDevice==='legion'?'Lenovo Legion · four zones':'Razer Basilisk V3';}}));
  for(const graph of document.querySelectorAll('[data-graph]')){
    graph.addEventListener('keydown',event=>{if(!graph.dataset.editor)return;const direction={ArrowUp:1,ArrowRight:1,ArrowDown:-1,ArrowLeft:-1}[event.key];if(direction){event.preventDefault();stepControl(graph.dataset.control,direction*Number($(graph.dataset.control).step||1));}});
    let dragging=false;
    const update=event=>{const box=graph.getBoundingClientRect(),fraction=Math.max(0,Math.min(1,(event.clientX-box.left-10)/Math.max(1,box.width-20)));const value=graph.dataset.editor && window.GlassSurface?.controlValue?window.GlassSurface.controlValue(graph.dataset.graph,draft,event.clientX-box.left,event.clientY-box.top,box.width,box.height):graph.dataset.editor?(graph.dataset.graph==='gain'?50+Math.max(0,Math.min(1,1-(event.clientY-box.top)/box.height))*150:graph.dataset.graph==='fall'?fraction*1000-90-draft.HoldMilliseconds:fraction*1000-90):graph.dataset.graph==='gain'?50+fraction*150:graph.dataset.graph==='fall'?fraction*700:fraction*900;setControlValue(graph.dataset.control,value);};
    graph.addEventListener('pointerdown',event=>{if(event.button!==0 || firstLoad)return;dragging=true;graph.setPointerCapture(event.pointerId);update(event);});graph.addEventListener('pointermove',event=>{if(dragging)update(event);});graph.addEventListener('pointerup',()=>{if(dragging){dragging=false;action('apply');}});graph.addEventListener('pointercancel',()=>{dragging=false;});
  }
  for(const dial of document.querySelectorAll('.brightness-dial')){
    let start=null;const input=$(dial.dataset.control);
    dial.addEventListener('pointerdown',event=>{if(event.button!==0 || firstLoad)return;start={y:event.clientY,value:Number(input.value)};dial.setPointerCapture(event.pointerId);});dial.addEventListener('pointermove',event=>{if(start)setControlValue(input.id,start.value+(start.y-event.clientY)/18);});dial.addEventListener('pointerup',()=>{if(start){start=null;if(dirty)action('apply');}});dial.addEventListener('pointercancel',()=>{start=null;});
    dial.addEventListener('keydown',event=>{const keys={ArrowUp:1,ArrowRight:1,ArrowDown:-1,ArrowLeft:-1};if(event.key in keys){event.preventDefault();setControlValue(input.id,Number(input.value)+keys[event.key],true);}else if(event.key==='Home' || event.key==='End'){event.preventDefault();setControlValue(input.id,Number(event.key==='Home'?input.min:input.max),true);}});
  }
}
const reducedMotion=matchMedia('(prefers-reduced-motion: reduce)');if(reducedMotion.matches)$('animatePreview').checked=false;
let lastPaint=0,displayFrame=null,studioScene=null;
async function pollLive(){try{const frame=await request('live');if(frame?.Version===3 && (!state.Live || Date.parse(frame.Timestamp)>=Date.parse(state.Live.Timestamp)))state.Live=frame;}catch{state.Live=null;}finally{setTimeout(pollLive,document.hidden?500:33);}}
function paint(now) {
  requestAnimationFrame(paint);if(now-lastPaint<(studio?25:40))return;lastPaint=now;
  const effect=effectId(),native=effect.startsWith('hp-'),candidate=state.Live;
  const valid=candidate?.Version===3 && candidate.Style===effect && (native?state.Active===effect:state.Running && Date.now()-Date.parse(candidate.Timestamp)<500);
  const animate=$('animatePreview').checked;if(animate || !displayFrame || displayFrame.Style!==effect || !valid)displayFrame=valid?candidate:null;const frame=displayFrame;
  if(studio){
    studioScene?.update(frame);if(!$('equalizerControls').hidden)for(const graph of document.querySelectorAll('[data-graph]'))window.Lighting3D?.drawGraph(graph,graph.dataset.graph,draft,frame);
    $('hpPreviewStatus').textContent=frame?.HpKeys?'Live key output':native?'On-device pattern':'Waiting for output';
    $('legionPreviewStatus').textContent=frame?.LegionColors?(frame.LegionBrightness===0?'Lights off':'Live zone colours'):native?'On-device pattern':'Waiting for output';
    $('razerPreviewStatus').textContent=frame && !frame.RazerNative?'Live colour output':'Native Chroma · external';
  }else{
    for(const key of keys){const lit=frame?.HpKeys?.[Math.min(124,key.column*6+key.row)]===7 && frame.HpBrightness>0;key.g.classList.toggle('lit',lit);key.g.style.opacity=lit?frame.HpBrightness/5:1;}
    [...$('zonePreview').querySelectorAll('.legion-keyboard i')].forEach((zone,i)=>{const rgb=frame?.LegionColors?.[i] || [0,0,0];zone.style.opacity=frame?.LegionBrightness===1?.5:frame?.LegionBrightness===0?0:frame?.LegionColors?1:0;zone.style.backgroundColor=`rgb(${rgb.join(',')})`;});
    const rgb=frame && !frame.RazerNative?frame.RazerRgb:null;for(const node of $('mousePreview').querySelectorAll('.mouse-edge,.mouse-wheel,.mouse-logo')){node.style.opacity=rgb?1:.1;node.style.stroke=rgb?`rgb(${rgb.join(',')})`:'#69717d';if(!node.classList.contains('mouse-edge'))node.style.fill=rgb?`rgb(${rgb.join(',')})`:'#69717d';}
  }
  $('previewSource').textContent=!animate?'Preview paused':frame?'Live lighting output':state.Running?'Connecting to lighting output':native?'On-device lighting':'Sync stopped';
}
keyboard();buildChoices();connectControls();renderControls();if(studio)studioScene=window.Lighting3D?.create($('deviceStage'));poll().then(pollLive);setInterval(()=>{if(!busy)poll();},900);requestAnimationFrame(paint);
// Expose state inspection for input-free verification; no input is generated.
window.lightingPreview={getSelected:()=>selected,getDraft:()=>structuredClone(draft),keyCount:()=>keys.length};
