(function(){
"use strict";

const API="/cws/vtcfarm";
const state={
  system:null,
  cwsOnline:false,
  lastHeartbeat:null,
  lastDefinitionHash:null,
  clientId:null,
  pollTimer:null,
  heartbeatTimer:null,
  pollInFlight:false,
  dspLive:false,
  dspLastFeedback:null,
  feedback:new Map(),
  logs:[],
  activeTab:"overview",
  logFilter:"all",
  logSearch:"",
  heartbeatAttempted:false,
  alerts:new Map(),
  alertFilter:"active",
  expandedDevices:new Set(),
  inspectorItems:new Map()
};

const MFE_LEVELS=["Critical","Error","Warning","Notice","Information","Verbose","Debug","Trace"];
const mfeDiagnostics={available:false,summary:null,logCursor:0,changeCursor:0,logTimer:null,changeTimer:null,stateTimer:null,farmStatusTimer:null,logBusy:false,changeBusy:false,stateBusy:false,farmStatusBusy:false,currentState:[],changes:[],farmStatus:null,logIds:new Set(),lastSync:null,errorAt:0};
const fieldSession={active:false,start:null,end:null,room:"",roomEvidence:"unknown",markers:[],steps:{},history:[],lastSnapshot:null,stopReason:"",result:"NOT STARTED",baseline:null};
const farmExpandedRooms=new Set();
const farmKnownRooms=new Set();
let mfeLogDisplayCutoff=0;

const $=(s,r=document)=>r.querySelector(s);
const $$=(s,r=document)=>Array.from(r.querySelectorAll(s));
const clean=v=>String(v==null?"":v).replace(/\r/g," ").trim();
const key=(device,control,property)=>[device,control,property].map(x=>clean(x).toLowerCase()).join("|");
const nowTime=()=>new Date().toLocaleTimeString([], {hour:"2-digit",minute:"2-digit",second:"2-digit",hour12:false});

async function request(method,path,body){
  const options={method,headers:{"Content-Type":"application/json"},cache:"no-store"};
  if(body!==undefined)options.body=JSON.stringify(body);
  const response=await fetch(API+path,options);
  let payload=null;
  try{payload=await response.json();}catch(_){ }
  if(!response.ok || (payload && payload.Success===false)){
    throw new Error(payload&&payload.Message?payload.Message:`CWS ${response.status}`);
  }
  return payload;
}

function mfeLevelNumber(value){
  if(typeof value==="number")return Math.max(0,Math.min(7,value));
  const text=clean(value).toLowerCase();
  const aliases={critical:0,error:1,warn:2,warning:2,notice:3,info:4,information:4,verbose:5,debug:6,trace:7};
  return Object.prototype.hasOwnProperty.call(aliases,text)?aliases[text]:4;
}
function log(level,message,detail){
  const n=mfeLevelNumber(level);
  const entry={time:new Date(),level:MFE_LEVELS[n].toLowerCase(),levelNumber:n,message:clean(message),detail:detail==null?"":typeof detail==="string"?detail:JSON.stringify(detail),source:"browser",sequence:0};
  state.logs.unshift(entry);
  if(state.logs.length>6500)state.logs.length=6000;
  renderLogs();
}

function hashDefinition(value){
  try{
    const s=JSON.stringify(value); let h=2166136261;
    for(let i=0;i<s.length;i++){h^=s.charCodeAt(i);h=Math.imul(h,16777619);}
    return (h>>>0).toString(16).padStart(8,"0");
  }catch(_){return "unknown";}
}

function getClientId(){
  if(!state.clientId)state.clientId="mav-vtc-farm-monitor-ui-"+Date.now().toString(36)+"-"+Math.random().toString(36).slice(2,9);
  return state.clientId;
}

function findMute(system,fader){
  if(!fader)return null;
  const buttons=((((system||{}).Dsp||{}).Buttons)||[]).filter(x=>clean(x.Type).toLowerCase()==="toggle");
  const normalizeWords=value=>clean(value).toLowerCase().replace(/[^a-z0-9]+/g," ").trim();
  const semanticStem=value=>normalizeWords(value).split(/\s+/).filter(word=>word&&!['mute','toggle','level','volume','gain','up','down','increment','decrement'].includes(word)).join(" ");
  const controlStem=value=>clean(value).toLowerCase().replace(/[^a-z0-9]+/g,"").replace(/(mute|gain|volume|level|toggle)$/g,"");
  const faderNameStem=semanticStem(fader.Name), faderControlStem=controlStem(fader.ControlId);
  const faderSmart=fader.SmartList&&fader.SmartList.SmartObjectId;
  let best=null,bestScore=-1;
  buttons.forEach(candidate=>{
    let score=0;
    const n=semanticStem(candidate.Name),c=controlStem(candidate.ControlId);
    if(faderNameStem&&n===faderNameStem)score+=100; else if(faderNameStem&&n&&(n.startsWith(faderNameStem)||faderNameStem.startsWith(n)))score+=60;
    if(faderControlStem&&c===faderControlStem)score+=90; else if(faderControlStem&&c&&(c.startsWith(faderControlStem)||faderControlStem.startsWith(c)))score+=50;
    if(faderSmart!=null&&candidate.SmartList&&candidate.SmartList.SmartObjectId===faderSmart)score+=25;
    if(score>bestScore){bestScore=score;best=candidate;}
  });
  return bestScore>0?best:null;
}

function buildSubscriptions(system){
  const result=[],seen=new Set();
  const add=(device,control,property)=>{
    device=clean(device);control=clean(control);property=clean(property);
    if(!device||!control||!property)return;
    const k=key(device,control,property);if(seen.has(k))return;seen.add(k);
    result.push({Device:device,Control:control,Property:property});
  };
  (system.UiSubscriptions||system.Subscriptions||[]).forEach(s=>add(s.Device||s.device,s.Control||s.control,s.Property||s.property));
  const dsp=system.Dsp||{};
  (dsp.Faders||[]).forEach(f=>{add("DSP",f.Name||f.ControlId,"value");const m=findMute(system,f);if(m)add("DSP",m.ControlId||m.Name,"value");});
  (dsp.Buttons||[]).filter(b=>clean(b.Type).toLowerCase()==="toggle").forEach(b=>add("DSP",b.ControlId||b.Name,"value"));
  (dsp.Devices||[]).forEach(d=>(d.Functions||[]).forEach(fn=>add("DSP",fn.ControlId||fn.Name,"value")));
  return result;
}

async function registerSubscriptions(){
  if(!state.system)return;
  const subscriptions=buildSubscriptions(state.system);
  let ok=0;
  for(const subscription of subscriptions){
    try{
      await request("POST","/subscriptions/subscribe",{ClientId:getClientId(),...subscription});
      ok++;
    }catch(error){log("warn",`Subscription failed: ${subscription.Device}/${subscription.Control}`,error.message);}
  }
  log("info",`Registered ${ok}/${subscriptions.length} subscriptions`);
}

function handleFeedback(update){
  if(!update)return;
  const device=clean(update.Device||update.device), control=clean(update.Control||update.control), property=clean(update.Property||update.property), value=update.Value!==undefined?update.Value:update.value;
  state.feedback.set(key(device,control,property),{value,time:new Date()});
  if(device.toLowerCase()==="dsp"){
    state.dspLive=true;state.dspLastFeedback=new Date();
    renderDsp();renderOverview();
  }
}

async function pollSubscriptions(){
  if(state.pollInFlight||!state.cwsOnline)return;
  state.pollInFlight=true;
  try{
    const response=await request("POST","/subscriptions/poll",{ClientId:getClientId()});
    if(response&&Array.isArray(response.Value))response.Value.forEach(handleFeedback);
  }catch(error){log("warn","Subscription poll failed",error.message);}
  finally{state.pollInFlight=false;}
}

function startSubscriptionPolling(){
  if(state.pollTimer)clearInterval(state.pollTimer);
  state.pollTimer=setInterval(pollSubscriptions,750);
  pollSubscriptions();
}

async function heartbeat(initial){
  state.heartbeatAttempted=true;
  try{
    const response=await request("GET","/system/definition/get");
    if(!response||response.Success!==true||!response.Value)throw new Error("SystemDefinition unavailable");
    const definition=response.Value;
    const hash=hashDefinition(definition);
    const changed=state.lastDefinitionHash&&state.lastDefinitionHash!==hash;
    state.system=definition;state.lastDefinitionHash=hash;state.cwsOnline=true;state.lastHeartbeat=new Date();
    if(initial)log("info",`Connected to ${clean(definition.Name)||"VTC Farm"}`);
    if(changed){log("warn","SystemDefinition changed; reloading monitor model",`Definition hash ${hash}`);await registerSubscriptions();}
    renderAll();
    return true;
  }catch(error){
    if(state.cwsOnline||initial)log("error","CWS heartbeat failed",error.message);
    state.cwsOnline=false;renderAll();return false;
  }
}

function startHeartbeat(){
  if(state.heartbeatTimer)clearInterval(state.heartbeatTimer);
  state.heartbeatTimer=setInterval(()=>heartbeat(false),10000);
}

function updateClock(){
  const d=new Date();
  $("#monitor-time").textContent=d.toLocaleTimeString([], {hour:"2-digit",minute:"2-digit",second:"2-digit",hour12:false});
  $("#monitor-date").textContent=d.toLocaleDateString("en-US",{day:"2-digit",month:"short",year:"numeric"});
}

function matrix(){return (state.system&&state.system.MatrixSwitcher)||{};}
function dsp(){return (state.system&&state.system.Dsp)||{};}
function counts(){
  const m=matrix(),d=dsp();
  const displays=(m.Decoders||[]).filter(x=>x.Display||['monitor','projector','display'].includes(clean(x.Type).toLowerCase())).length;
  return {sources:(m.Encoders||[]).length,destinations:(m.Decoders||[]).length,displays,cameras:((state.system&&state.system.Cameras)||[]).length,dspFaders:(d.Faders||[]).length,dspButtons:(d.Buttons||[]).length,dspDevices:(d.Devices||[]).length};
}

function setStatusClass(element,status){
  if(!element)return;
  ['status-good','status-warn','status-bad','status-info','status-unknown'].forEach(c=>element.classList.remove(c));
  element.classList.add('status-'+status);
}

function monitoringState(){
  if(!state.cwsOnline)return {status:'bad',title:'CWS Offline',detail:'The monitor cannot reach the VTC Farm CWS API.'};
  if(!state.system)return {status:'warn',title:'Connecting',detail:'CWS is reachable but the VTC Farm SystemDefinition has not loaded.'};
  const c=counts();
  if(!c.sources && !c.destinations)return {status:'warn',title:'Farm Definition Empty',detail:'CWS is online, but no VTC Farm encoders or decoders are currently exposed.'};
  return {status:'good',title:'Monitoring Online',detail:`VTC Farm topology loaded: ${c.sources} encoders / ${c.destinations} decoders.`};
}

function renderHeader(){
  const room=clean(state.system&&state.system.Name)||'VTC Farm';
  $("#monitor-room-name").textContent=room;
  $("#footer-room").textContent=room;
  const h=monitoringState(),pill=$("#monitor-health-pill");setStatusClass(pill,h.status);pill.querySelector('span').textContent=h.title;
  const f=$("#footer-cws-dot");setStatusClass(f,state.cwsOnline?'good':'bad');$("#footer-cws").textContent=state.cwsOnline?'CWS online':'CWS offline';
  $("#footer-heartbeat").textContent=state.lastHeartbeat?nowTimeFor(state.lastHeartbeat):'Never';
}
function nowTimeFor(d){return d.toLocaleTimeString([], {hour:"2-digit",minute:"2-digit",second:"2-digit",hour12:false});}

function alertCandidates(){
  const result=[],s=state.system||{};
  const add=(id,severity,device,title,detail)=>result.push({id,severity,device:clean(device)||'System',title:clean(title),detail:clean(detail)});
  if(state.heartbeatAttempted&&!state.cwsOnline)add('cws-offline','error','Control Processor','CWS is unreachable','SystemDefinition and subscription polling are unavailable.');
  if(state.system&&s.MatrixSwitcher){
    (s.MatrixSwitcher.Encoders||[]).filter(x=>!clean(x.VideoStreamLocation)&&clean(x.Name).toLowerCase()!=='blank').forEach((x,i)=>add(`encoder-no-rtsp-${clean(x.Name)||i}`,'warn',clean(x.Name)||`Encoder ${i+1}`,'RTSP location is not configured','The source exists in SystemDefinition, but VideoStreamLocation is blank.'));
    (s.MatrixSwitcher.Decoders||[]).filter(x=>x.Display&&!x.Display.ComPort&&!x.Display.IpAddress).forEach((x,i)=>add(`display-no-control-${clean(x.Name)||i}`,'warn',clean(x.Name)||`Display ${i+1}`,'Display control path is not visible','No display COM port or IP address is present in SystemDefinition.'));
  }
  return result;
}

function refreshAlertModel(){
  const now=new Date(),current=alertCandidates(),currentIds=new Set(current.map(x=>x.id));
  state.alerts.forEach((entry,id)=>{if(entry.active&&!currentIds.has(id)){entry.active=false;entry.resolvedAt=now;entry.lastSeen=now;}});
  current.forEach(candidate=>{
    let entry=state.alerts.get(candidate.id);
    if(!entry){entry={...candidate,firstSeen:now,lastSeen:now,active:true,acknowledged:false,resolvedAt:null};state.alerts.set(candidate.id,entry);log('warn',`Alert opened: ${candidate.title}`,candidate.detail);return;}
    if(!entry.active){entry.firstSeen=now;entry.acknowledged=false;entry.resolvedAt=null;log('warn',`Alert reopened: ${candidate.title}`,candidate.detail);}
    Object.assign(entry,candidate,{active:true,lastSeen:now});
  });
  if(state.alerts.size>150){Array.from(state.alerts.entries()).filter(([,x])=>!x.active).sort((a,b)=>a[1].lastSeen-b[1].lastSeen).slice(0,state.alerts.size-150).forEach(([id])=>state.alerts.delete(id));}
}

function severityLabel(severity){return severity==='error'?'ERROR':severity==='warn'?'WARNING':severity==='info'?'INFO':'NOTICE';}
function alertStatusClass(severity){return severity==='error'?'bad':severity==='warn'?'warn':severity==='info'?'info':'unknown';}
function alertTime(value){return value?value.toLocaleString([], {hour12:false}):'—';}
function activeAlerts(){return Array.from(state.alerts.values()).filter(x=>x.active);}

function renderAlerts(){
  const all=Array.from(state.alerts.values()).sort((a,b)=>Number(b.active)-Number(a.active)||Number(a.acknowledged)-Number(b.acknowledged)||b.lastSeen-a.lastSeen),active=all.filter(x=>x.active),unack=active.filter(x=>!x.acknowledged);
  $('#alert-active-count').textContent=active.length;$('#alert-unack-count').textContent=unack.length;$('#alert-error-count').textContent=active.filter(x=>x.severity==='error').length;$('#alert-warn-count').textContent=active.filter(x=>x.severity==='warn').length;
  const badge=$('#monitor-alert-badge');badge.hidden=!unack.length;badge.textContent=unack.length;
  const filtered=state.alertFilter==='all'?all:state.alertFilter==='unack'?unack:active;
  $('#alert-list').innerHTML=filtered.length?filtered.map(x=>`<article class="alert-card ${x.active?'is-active':'is-resolved'} ${x.acknowledged?'is-acknowledged':''}"><div class="alert-severity status-${alertStatusClass(x.severity)}"><span class="status-dot status-${alertStatusClass(x.severity)}"></span>${severityLabel(x.severity)}</div><div class="alert-content"><div class="alert-title-row"><div><span class="alert-device">${escapeHtml(x.device)}</span><h3>${escapeHtml(x.title)}</h3></div><span class="alert-state">${x.active?(x.acknowledged?'Acknowledged':'Active'):'Resolved'}</span></div><p>${escapeHtml(x.detail)}</p><div class="alert-times"><span>First seen <strong>${escapeHtml(alertTime(x.firstSeen))}</strong></span><span>Last seen <strong>${escapeHtml(alertTime(x.lastSeen))}</strong></span>${x.resolvedAt?`<span>Resolved <strong>${escapeHtml(alertTime(x.resolvedAt))}</strong></span>`:''}</div></div><div class="alert-actions">${x.active?`<button data-alert-ack="${escapeAttr(x.id)}">${x.acknowledged?'Unacknowledge':'Acknowledge'}</button>`:''}</div></article>`).join(''):'<div class="empty-state">No alerts match this view.</div>';
}

function renderOverview(){
  const h=monitoringState();
  $("#overview-health").textContent=h.title;$("#overview-health-detail").textContent=h.detail;setStatusClass($("#overview-health-dot"),h.status);
  $("#overview-cws").textContent=state.cwsOnline?'Online':'Offline';$("#overview-cws-detail").textContent=state.lastHeartbeat?`Heartbeat ${nowTimeFor(state.lastHeartbeat)}`:'No heartbeat';setStatusClass($("#overview-cws-dot"),state.cwsOnline?'good':'bad');
  $("#overview-config").textContent=state.system?'Loaded':'Unavailable';$("#overview-config-detail").textContent=state.system?`Definition ${state.lastDefinitionHash}`:'No SystemDefinition';setStatusClass($("#overview-config-dot"),state.system?'good':'bad');
  const topology=counts();
  $("#overview-dsp").textContent=state.system?`${topology.sources + topology.destinations} Devices`:'Waiting';
  $("#overview-dsp-detail").textContent=state.system?`${topology.sources} encoders / ${topology.destinations} decoders`:'Waiting for Farm SystemDefinition';
  setStatusClass($("#overview-dsp-dot"),state.system?(topology.sources||topology.destinations?'info':'warn'):'unknown');
  setStatusClass($("#overview-cws-dot"),state.cwsOnline?'good':'bad');

  const alert=$("#overview-alert");setStatusClass(alert,h.status==='good'?'info':h.status);$("#overview-alert-title").textContent=h.title;$("#overview-alert-copy").textContent=h.detail;
  $("#summary-updated").textContent=state.lastHeartbeat?`Updated ${nowTimeFor(state.lastHeartbeat)}`:'Never updated';
  const c=counts(),s=state.system||{};
  const summary=[['Farm',clean(s.Name)||'VTC Farm'],['Encoders',c.sources],['Decoders',c.destinations],['NVX Devices',c.sources+c.destinations],['CWS Root',API],['Definition',state.lastDefinitionHash||'—']];
  $("#summary-grid").innerHTML=summary.map(([label,value])=>`<div class="summary-item"><span>${label}</span><strong>${value}</strong></div>`).join('');

  const subs=[
    ['VTC Farm CWS',state.cwsOnline?'good':'bad',state.cwsOnline?'Online at '+API:'Offline'],
    ['NVX Encoders',c.sources?'info':'warn',c.sources?`${c.sources} exposed by Farm`:'None exposed'],
    ['NVX Decoders',c.destinations?'info':'warn',c.destinations?`${c.destinations} exposed by Farm`:'None exposed'],
    ['Monitor Mode','info','Read only POC'],
    ['Allocator / Rooms','unknown','Phase 2 telemetry not exposed yet']
  ];
  $("#subsystem-list").innerHTML=subs.map(([name,status,text])=>`<div class="subsystem-row"><span class="status-dot status-${status}"></span><strong>${escapeHtml(name)}</strong><span>${escapeHtml(text)}</span></div>`).join('');

  const issues=activeAlerts();
  $("#issue-count").textContent=`${issues.length} active alert${issues.length===1?'':'s'}`;
  $("#issue-list").innerHTML=issues.length?issues.slice(0,8).map(x=>`<div class="issue-row"><span class="status-dot status-${alertStatusClass(x.severity)}"></span><div><strong>${escapeHtml(x.title)}</strong><span>${escapeHtml(x.device)} · ${escapeHtml(x.detail)}</span></div></div>`).join(''):'<div class="empty-state">No active monitor-side alerts.</div>';
}

function deviceInventory(){
  const s=state.system||{},m=s.MatrixSwitcher||{},items=[];
  const push=(item)=>{item.id=[item.category,item.name,item.kind||'device'].map(clean).join('|');items.push(item);};
  push({category:'Core',name:'Control Processor',type:'MAV / CWS',detail:state.cwsOnline?'CWS responding':'CWS unreachable',status:state.cwsOnline?'good':'bad',kind:'processor',source:{Name:clean(s.Name)||'VTC Farm',CwsApi:API}});
  if(s.Dsp)push({category:'Audio',name:clean(s.Dsp.Name)||'DSP',type:clean(s.Dsp.Type)||'DSP',detail:clean(s.Dsp.IpAddress)||'No IP in definition',status:state.dspLive?'good':'unknown',kind:'dsp',source:s.Dsp});
  if(s.VTC)push({category:'Conferencing',name:clean(s.VTC.Name)||'VTC',type:[clean(s.VTC.Brand),clean(s.VTC.Type)].filter(Boolean).join(' • '),detail:connectionDetail(s.VTC),status:'unknown',kind:'vtc',source:s.VTC});
  if(s.ATC)push({category:'Conferencing',name:clean(s.ATC.Name)||'ATC',type:[clean(s.ATC.Brand),clean(s.ATC.Type)].filter(Boolean).join(' • '),detail:connectionDetail(s.ATC),status:'unknown',kind:'atc',source:s.ATC});
  if(s.VtcFarm&&s.VtcFarm.Enabled)push({category:'Conferencing',name:'VTC Farm',type:`EISC ${clean(s.VtcFarm.IPID)}`,detail:clean(s.VtcFarm.IpAddress)||'No IP in definition',status:'unknown',kind:'vtcfarm',source:s.VtcFarm});
  (m.Encoders||[]).forEach((x,i)=>push({category:'NVX Source',name:clean(x.Name)||`Encoder ${i+1}`,type:`Encoder • ${clean(x.Type)||'Source'}`,detail:[clean(x.IPID),clean(x.VideoStreamLocation)].filter(Boolean).join(' • ')||'Configured',status:'unknown',kind:'encoder',source:x}));
  (m.Decoders||[]).forEach((x,i)=>push({category:x.Display?'Display':'NVX Destination',name:clean(x.Name)||`Decoder ${i+1}`,type:x.Display?`${clean(x.Display.Brand)} ${clean(x.Display.Type)}`.trim():`Decoder • ${clean(x.Type)||'Destination'}`,detail:x.Display?connectionDetail(x.Display):clean(x.IPID)||'Configured',status:'unknown',kind:x.Display?'display':'decoder',source:x}));
  (s.Cameras||[]).forEach((x,i)=>push({category:'Camera',name:clean(x.Name)||`Camera ${i+1}`,type:[clean(x.Brand),clean(x.Type)].filter(Boolean).join(' • '),detail:connectionDetail(x),status:'unknown',kind:'camera',source:x}));
  ((s.Dsp||{}).Devices||[]).forEach((x,i)=>push({category:'DSP Device',name:clean(x.Name)||`DSP Device ${i+1}`,type:clean(x.Type)||'DSP Device',detail:`${(x.Functions||[]).length} function${(x.Functions||[]).length===1?'':'s'}`,status:'unknown',kind:'dsp-device',source:x}));
  const duplicates=new Map();items.forEach(x=>{const base=x.id,n=(duplicates.get(base)||0)+1;duplicates.set(base,n);if(n>1)x.id=`${base}|${n}`;});
  return items;
}
function connectionDetail(x){
  if(clean(x&&x.IpAddress))return `IP ${clean(x.IpAddress)}${x.Port?':'+x.Port:''}`;
  if(x&&x.SerialPort)return `Serial ${clean(x.SerialPort.Device)||'ControlSystem'} COM${x.SerialPort.Port}`;
  if(x&&x.ComPort)return `COM${x.ComPort}`;
  return clean(x&&x.CommunicationMethod)||'Configured';
}
function valueType(value){
  if(Array.isArray(value))return 'List';
  if(value===null)return 'Null';
  if(value instanceof Date)return 'Date';
  const t=typeof value;
  if(t==='object')return 'Object';
  if(t==='boolean')return 'Boolean';
  if(t==='number')return 'Number';
  if(t==='string')return 'String';
  return t ? t.charAt(0).toUpperCase()+t.slice(1) : 'Value';
}
function valueSummary(value){
  if(Array.isArray(value))return `${value.length} item${value.length===1?'':'s'}`;
  if(value&&typeof value==='object')return `${Object.keys(value).length} propert${Object.keys(value).length===1?'y':'ies'}`;
  if(value===null)return 'null';
  if(value===undefined)return 'undefined';
  if(value==='')return 'Empty string';
  return String(value);
}
function inspectableProperties(value,prefix='',depth=0,result=[]){
  if(depth>5||value==null)return result;
  if(Array.isArray(value)){
    result.push({name:prefix||'Value',display:valueSummary(value),raw:value,type:'List'});
    value.forEach((item,i)=>{
      const next=prefix?`${prefix}[${i}]`:`[${i}]`;
      if(Array.isArray(item))inspectableProperties(item,next,depth+1,result);
      else if(item&&typeof item==='object')Object.keys(item).sort().forEach(k=>{
        const v=item[k],path=`${next}.${k}`;
        if(v&&typeof v==='object')inspectableProperties(v,path,depth+1,result);
        else if(v!==undefined&&v!==null&&String(v)!=='')result.push({name:path,display:valueSummary(v),raw:v,type:valueType(v)});
      });
      else result.push({name:next,display:valueSummary(item),raw:item,type:valueType(item)});
    });
    return result;
  }
  if(typeof value==='object'){
    Object.keys(value).sort().forEach(k=>{
      const v=value[k],next=prefix?`${prefix}.${k}`:k;
      if(Array.isArray(v))inspectableProperties(v,next,depth+1,result);
      else if(v&&typeof v==='object')inspectableProperties(v,next,depth+1,result);
      else if(v!==undefined&&v!==null&&String(v)!=='')result.push({name:next,display:valueSummary(v),raw:v,type:valueType(v)});
    });
    return result;
  }
  result.push({name:prefix||'Value',display:valueSummary(value),raw:value,type:valueType(value)});return result;
}
function deviceLiveProperties(item){
  const rows=[];
  if(item.kind==='processor'){
    rows.push(['CWS',state.cwsOnline?'Online':'Offline']);
    rows.push(['Last heartbeat',state.lastHeartbeat?alertTime(state.lastHeartbeat):'Never']);
    rows.push(['Definition hash',state.lastDefinitionHash||'—']);
  }else if(item.kind==='dsp'){
    rows.push(['Feedback',state.dspLive?'Live':'No live feedback']);
    rows.push(['Last feedback',state.dspLastFeedback?alertTime(state.dspLastFeedback):'Never']);
    (dsp().Faders||[]).forEach(f=>{const fb=feedbackForFader(f);rows.push([`Fader · ${clean(f.Name)||clean(f.ControlId)}`,fb?String(fb.value):'No data']);const mute=findMute(state.system||{},f),mfb=feedbackForControl(mute);if(mute)rows.push([`Mute · ${clean(mute.Name)||clean(mute.ControlId)}`,mfb?String(mfb.value):'No data']);});
  }else if(item.kind==='dsp-device'){
    (item.source.Functions||[]).forEach(fn=>{const fb=feedbackForControl(fn);rows.push([clean(fn.Name)||clean(fn.ControlId)||'Function',fb?String(fb.value):'No data']);});
  }else{
    rows.push(['Live telemetry','Not exposed by current CWS contract']);
  }
  return rows;
}
function registerInspectorItem(deviceId,source,row,index){
  const normalized=Array.isArray(row)?{name:row[0],display:valueSummary(row[1]),raw:row[1],type:valueType(row[1])}:row;
  const id=`${deviceId}|${source}|${index}`;
  state.inspectorItems.set(id,{...normalized,deviceId,source});
  return id;
}
function propertyRowsHtml(rows,empty,deviceId,source){
  return rows.length?rows.map((row,index)=>{
    const normalized=Array.isArray(row)?{name:row[0],display:valueSummary(row[1]),raw:row[1],type:valueType(row[1])}:row;
    const id=registerInspectorItem(deviceId,source,normalized,index);
    const summary=normalized.display==null?valueSummary(normalized.raw):String(normalized.display);
    return `<button class="device-property-row device-property-action" type="button" data-inspect-id="${escapeAttr(id)}" title="Inspect ${escapeAttr(normalized.name)}"><span>${escapeHtml(normalized.name)}</span><strong>${escapeHtml(summary)}</strong><i aria-hidden="true">›</i></button>`;
  }).join(''):`<div class="device-property-empty">${escapeHtml(empty||'No values available.')}</div>`;
}
function ensureInspector(){
  let modal=$('#property-inspector');
  if(modal)return modal;
  document.body.insertAdjacentHTML('beforeend',`<div id="property-inspector" class="property-inspector" hidden><div class="property-inspector-backdrop" data-inspector-close></div><section class="property-inspector-panel" role="dialog" aria-modal="true" aria-labelledby="property-inspector-title"><header><div><span id="property-inspector-source">DETAILS</span><h2 id="property-inspector-title">Property</h2></div><button type="button" class="property-inspector-close" data-inspector-close aria-label="Close property inspector">×</button></header><div id="property-inspector-meta" class="property-inspector-meta"></div><div id="property-inspector-content" class="property-inspector-content"></div></section></div>`);
  modal=$('#property-inspector');
  modal.addEventListener('click',e=>{if(e.target.closest('[data-inspector-close]'))closeInspector();});
  document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!modal.hidden)closeInspector();});
  return modal;
}
function renderInspectorValue(value,path='',depth=0){
  if(depth>6)return `<div class="inspector-value inspector-muted">Maximum detail depth reached.</div>`;
  if(Array.isArray(value)){
    if(!value.length)return `<div class="inspector-empty-list">This list is empty.</div>`;
    return `<div class="inspector-list">${value.map((item,index)=>{
      const itemPath=path?`${path}[${index}]`:`[${index}]`,type=valueType(item),summary=valueSummary(item);
      if(item&&typeof item==='object')return `<details class="inspector-list-item" open><summary><span>${escapeHtml(`[${index}]`)}</span><strong>${escapeHtml(summary)}</strong><em>${escapeHtml(type)}</em></summary><div class="inspector-nested">${renderInspectorValue(item,itemPath,depth+1)}</div></details>`;
      return `<div class="inspector-list-item inspector-list-scalar"><span>${escapeHtml(`[${index}]`)}</span><strong>${escapeHtml(summary)}</strong><em>${escapeHtml(type)}</em></div>`;
    }).join('')}</div>`;
  }
  if(value&&typeof value==='object'){
    const keys=Object.keys(value).sort();
    if(!keys.length)return `<div class="inspector-value inspector-muted">Empty object.</div>`;
    return `<div class="inspector-object">${keys.map(k=>{
      const v=value[k],childPath=path?`${path}.${k}`:k;
      if(v&&typeof v==='object')return `<details class="inspector-object-item" open><summary><span>${escapeHtml(k)}</span><strong>${escapeHtml(valueSummary(v))}</strong><em>${escapeHtml(valueType(v))}</em></summary><div class="inspector-nested">${renderInspectorValue(v,childPath,depth+1)}</div></details>`;
      return `<div class="inspector-object-item inspector-object-scalar"><span>${escapeHtml(k)}</span><strong>${escapeHtml(valueSummary(v))}</strong><em>${escapeHtml(valueType(v))}</em></div>`;
    }).join('')}</div>`;
  }
  return `<pre class="inspector-value">${escapeHtml(valueSummary(value))}</pre>`;
}
function openInspector(id){
  const item=state.inspectorItems.get(id);if(!item)return;
  const modal=ensureInspector();
  $('#property-inspector-source').textContent=item.source==='configuration'?'SYSTEMDEFINITION':'CWS / RUNTIME';
  $('#property-inspector-title').textContent=item.name||'Property';
  $('#property-inspector-meta').innerHTML=`<div><span>TYPE</span><strong>${escapeHtml(item.type||valueType(item.raw))}</strong></div><div><span>PATH</span><strong>${escapeHtml(item.name||'Value')}</strong></div><div><span>DEVICE</span><strong>${escapeHtml(item.deviceId||'—')}</strong></div>`;
  $('#property-inspector-content').innerHTML=renderInspectorValue(item.raw,item.name||'',0);
  modal.hidden=false;document.body.classList.add('inspector-open');
  $('.property-inspector-close',modal)?.focus();
}
function closeInspector(){const modal=$('#property-inspector');if(!modal)return;modal.hidden=true;document.body.classList.remove('inspector-open');}


function renderDevices(){
  const items=deviceInventory(),categories=['All',...Array.from(new Set(items.map(x=>x.category)))],signature=categories.join('|');
  const group=$("#device-filter-group");
  if(!group.dataset.ready){group.dataset.ready='1';group.addEventListener('click',e=>{const b=e.target.closest('[data-device-filter]');if(!b)return;$$('[data-device-filter]',group).forEach(x=>x.classList.toggle('is-active',x===b));paintDeviceGrid(b.dataset.deviceFilter);});}
  if(group.dataset.signature!==signature){const prior=(group.querySelector('.is-active')||{}).dataset?.deviceFilter||'All';group.dataset.signature=signature;group.innerHTML=categories.map(x=>`<button class="${x===prior?'is-active':''}" data-device-filter="${escapeAttr(x)}">${escapeHtml(x)}</button>`).join('');if(!group.querySelector('.is-active'))group.querySelector('[data-device-filter]')?.classList.add('is-active');}
  paintDeviceGrid((group.querySelector('.is-active')||{}).dataset?.deviceFilter||'All');
}
function paintDeviceGrid(filter){
  const items=deviceInventory().filter(x=>filter==='All'||x.category===filter);
  state.inspectorItems.clear();
  $("#device-grid").innerHTML=items.map(x=>{const expanded=state.expandedDevices.has(x.id),config=inspectableProperties(x.source),live=deviceLiveProperties(x);return `<article class="device-card ${expanded?'is-expanded':''}" data-device-id="${escapeAttr(x.id)}"><button class="device-card-summary" type="button" aria-expanded="${expanded?'true':'false'}"><div class="device-card-top"><span class="device-icon">${deviceGlyph(x.category)}</span><span class="health-mini status-${x.status}"><i></i>${x.status==='good'?'Online':x.status==='bad'?'Offline':x.status==='info'?'Active':'Configured'}</span></div><span class="device-category">${escapeHtml(x.category)}</span><h3>${escapeHtml(x.name||'Unnamed')}</h3><p>${escapeHtml(x.type||'Device')}</p><small>${escapeHtml(x.detail||'Configured')}</small><span class="device-expand-label">${expanded?'Hide details':'Show properties & values'} <i>⌄</i></span></button><div class="device-details" ${expanded?'':'hidden'}><div class="device-detail-column"><div class="device-detail-heading"><span>CONFIGURATION</span><strong>SystemDefinition</strong></div>${propertyRowsHtml(config,'No configuration properties exposed.',x.id,'configuration')}</div><div class="device-detail-column"><div class="device-detail-heading"><span>LIVE VALUES</span><strong>CWS / Runtime</strong></div>${propertyRowsHtml(live,'No live values available.',x.id,'live')}</div></div></article>`;}).join('')||'<div class="empty-state">No devices in this category.</div>';
}
function deviceGlyph(cat){return cat.includes('Display')?'▣':cat.includes('NVX')?'◆':cat.includes('Audio')||cat.includes('DSP')?'≋':cat.includes('Camera')?'◉':cat.includes('Conference')?'◇':'⬡';}

function renderRoutes(){
  const enc=matrix().Encoders||[],dec=matrix().Decoders||[];
  $("#route-source-count").textContent=`${enc.length} configured`;$("#route-dest-count").textContent=`${dec.length} configured`;
  $("#route-sources").innerHTML=enc.map(x=>routeItem(x,'source')).join('')||'<div class="empty-state">No encoders configured.</div>';
  $("#route-destinations").innerHTML=dec.map(x=>routeItem(x,'dest')).join('')||'<div class="empty-state">No decoders configured.</div>';
  $("#route-fabric-status").textContent=state.system&&state.system.MatrixSwitcher?`${clean(matrix().Type)||'Matrix'} topology loaded`:'Not configured';
  const rows=enc.map(x=>['Source',clean(x.Name),clean(x.IPID)||'—',clean(x.VideoStreamLocation)||'—',clean(x.VideoMulticastAddress)||'—',clean(x.UsbLocalId)||'—']).concat(dec.map(x=>['Destination',clean(x.Name),clean(x.IPID)||'—','—','—',clean(x.UsbLocalId)||'—']));
  $("#route-detail-table").innerHTML=dataTable(['Role','Name','IP ID','RTSP','Multicast','USB Local ID'],rows);
}
function routeItem(x,role){const stream=clean(x.VideoStreamLocation);return `<div class="route-item"><span class="route-item-icon">${role==='source'?'▶':'▣'}</span><div><strong>${escapeHtml(clean(x.Name)||'Unnamed')}</strong><small>${escapeHtml(clean(x.Type)||role)}</small></div><div class="route-item-meta"><span>${escapeHtml(clean(x.IPID)||'No IP ID')}</span><em class="status-${stream||role==='dest'?'info':'unknown'}">${role==='source'?(stream?'RTSP configured':'No RTSP'):'Configured'}</em></div></div>`;}

function feedbackForFader(fader){
  if(!fader)return null;
  const names=[fader.Name,fader.ControlId].filter(Boolean);
  for(const n of names){const f=state.feedback.get(key('DSP',n,'value'));if(f)return f;}
  return null;
}
function feedbackForControl(control){return control?state.feedback.get(key('DSP',control.ControlId||control.Name,'value'))||null:null;}
function normalizePercent(value){const n=Number(value);return Number.isFinite(n)?Math.max(0,Math.min(100,n)):0;}
function boolValue(value){return value===true||Number(value)===1||['true','on','muted','mute'].includes(clean(value).toLowerCase());}

function isMicrophoneAudioControl(fader,mute){
  const text=[
    fader&&fader.Name,
    fader&&fader.ControlId,
    mute&&mute.Name,
    mute&&mute.ControlId
  ].map(clean).join(' ').toLowerCase();
  return /(^|[^a-z])(mic|microphone|microphones)([^a-z]|$)/.test(text);
}

function dspMuteIconSvg(isMicrophone){
  if(isMicrophone){
    return '<svg class="mute-symbol mute-symbol-microphone" viewBox="0 0 24 24" aria-hidden="true"><path d="M9 5.5V10a3 3 0 0 0 4.9 2.32M15 10V5.5a3 3 0 0 0-5.45-1.75M6.5 9.5V10a5.5 5.5 0 0 0 9.16 4.12M17.5 9.5V10a5.48 5.48 0 0 1-.63 2.55M12 15.5V20M9 20h6M4 4l16 16"/></svg>';
  }
  return '<svg class="mute-symbol mute-symbol-speaker" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 9v6h4l5 4V5L9 9H5zM17 9l4 4M21 9l-4 4M3.5 3.5l17 17"/></svg>';
}

function renderDsp(){
  const d=dsp(),faders=d.Faders||[],buttons=d.Buttons||[];
  const pill=$("#dsp-connection-pill");setStatusClass(pill,!state.system||!state.system.Dsp?'unknown':state.dspLive?'good':'warn');pill.querySelector('span').textContent=!state.system||!state.system.Dsp?'Not configured':state.dspLive?'Live feedback':'Waiting for feedback';
  $("#dsp-summary").innerHTML=[['DSP',clean(d.Name)||'—'],['Type',clean(d.Type)||'—'],['Address',clean(d.IpAddress)||'—'],['Faders',faders.length],['Buttons',buttons.length],['Last feedback',state.dspLastFeedback?nowTimeFor(state.dspLastFeedback):'Never']].map(([l,v])=>`<div><span>${escapeHtml(l)}</span><strong>${escapeHtml(String(v))}</strong></div>`).join('');
  $("#dsp-controls").innerHTML=faders.map(f=>{
    const fb=feedbackForFader(f),mute=findMute(state.system||{},f),mfb=feedbackForControl(mute),pct=fb?normalizePercent(fb.value):0,live=!!fb;
    const isMicMute=isMicrophoneAudioControl(f,mute),muted=!!(mfb&&boolValue(mfb.value));
    const muteStateClass=mfb?(muted?'is-muted':'is-unmuted'):'is-unknown';
    const muteKindClass=isMicMute?'is-microphone':'is-speaker';
    return `<article class="dsp-card ${live?'is-live':''}"><div class="dsp-card-head"><div><span>${escapeHtml(clean(f.ControlId)||'Fader')}</span><h3>${escapeHtml(clean(f.Name)||'Unnamed Fader')}</h3></div><span class="health-mini status-${live?'good':'unknown'}"><i></i>${live?'Live':'No data'}</span></div><div class="dsp-value-row"><strong>${live?Math.round(pct)+'%':'—'}</strong><span>${fb?`Updated ${nowTimeFor(fb.time)}`:'Waiting for subscription'}</span></div><div class="level-track" style="--level:${pct}%"><i></i></div>${mute?`<div class="mute-state ${muteStateClass} ${muteKindClass}"><span class="mute-icon" title="${isMicMute?'Microphone mute':'Program audio mute'}">${dspMuteIconSvg(isMicMute)}</span><div><strong>${mfb?(muted?'Muted':'Unmuted'):'Mute state unknown'}</strong><small>${escapeHtml(clean(mute.ControlId)||clean(mute.Name))}</small></div></div>`:'<div class="mute-state no-mute"><span class="mute-icon mute-icon-none">—</span><div><strong>No paired mute</strong><small>Not present in configuration</small></div></div>'}</article>`;
  }).join('')||'<div class="empty-state">No DSP faders configured.</div>';

  const functionRows=[];
  (d.Devices||[]).forEach(device=>(device.Functions||[]).forEach(fn=>{const fb=feedbackForControl(fn);functionRows.push([clean(device.Name),clean(device.Type),clean(fn.Name),clean(fn.ControlId),fb?String(fb.value):'—',fb?'Live':'No data']);}));
  $("#dsp-functions").innerHTML=functionRows.length?dataTable(['Device','Type','Function','Control ID','Value','State'],functionRows):'<div class="empty-state">No DSP device functions configured.</div>';
}

function mfeBrief(value){if(value==null)return "null";if(typeof value==="string"){try{const parsed=JSON.parse(value);return typeof parsed==="string"?parsed:JSON.stringify(parsed);}catch(_){return value;}}try{return JSON.stringify(value);}catch(_){return String(value);}}
function mfeDiagnosticFailure(error){mfeDiagnostics.available=false;if(Date.now()-mfeDiagnostics.errorAt>45000){mfeDiagnostics.errorAt=Date.now();log("warn","MAV diagnostics feed unavailable",error&&error.message||String(error));}mfeRenderDiagnosticStatus();}
async function mfeReadSummary(){const payload=await request("GET","/system/diagnostics/summary");if(!payload||!payload.Value)throw new Error("Diagnostics summary missing Value");return payload.Value;}
function mfeResetCursors(summary){const prev=mfeDiagnostics.summary;if(prev&&(Number(summary.LogSequence||0)<Number(prev.LogSequence||0)||Number(summary.StateSequence||0)<Number(prev.StateSequence||0))){mfeDiagnostics.logCursor=0;mfeDiagnostics.changeCursor=0;mfeDiagnostics.logIds.clear();mfeDiagnostics.changes=[];mfeDiagnostics.currentState=[];state.logs=state.logs.filter(x=>x.source!=="program");log("warn","SDK diagnostic sequence reset","VTC Farm program or SDK journal restarted; rebuilding evidence.");}mfeDiagnostics.summary=summary;}
function mfeAddProgramLog(item){const sequence=Number(item.Sequence||0),id="program:"+sequence;if(mfeDiagnostics.logIds.has(id))return;mfeDiagnostics.logIds.add(id);const n=mfeLevelNumber(item.Level);let time=item.Timestamp?new Date(item.Timestamp):new Date();if(Number.isNaN(+time))time=new Date();state.logs.unshift({time,level:MFE_LEVELS[n].toLowerCase(),levelNumber:n,message:String(item.Message||""),detail:"",source:"program",sequence,multiline:!!item.IsMultiline});if(state.logs.length>6500){const removed=state.logs.splice(6000);removed.forEach(x=>{if(x.source==="program")mfeDiagnostics.logIds.delete("program:"+x.sequence);});}}
async function mfePollLogs(){if(mfeDiagnostics.logBusy||!state.cwsOnline)return;mfeDiagnostics.logBusy=true;try{const summary=await mfeReadSummary();mfeResetCursors(summary);let pages=0,count=0;while(pages++<8){const response=await request("POST","/system/diagnostics/logs",{AfterSequence:mfeDiagnostics.logCursor,MaxEntries:750});const entries=Array.isArray(response.Value)?response.Value:[];if(!entries.length)break;for(const item of entries){const seq=Number(item.Sequence||0);if(seq>mfeDiagnostics.logCursor)mfeDiagnostics.logCursor=seq;mfeAddProgramLog(item);}count+=entries.length;if(entries.length<750)break;}mfeDiagnostics.available=true;mfeDiagnostics.lastSync=new Date();if(count)renderLogs();mfeRenderDiagnosticStatus();}catch(error){mfeDiagnosticFailure(error);}finally{mfeDiagnostics.logBusy=false;}}
async function mfePollChanges(){if(mfeDiagnostics.changeBusy||!state.cwsOnline)return;mfeDiagnostics.changeBusy=true;try{const summary=mfeDiagnostics.summary||await mfeReadSummary();mfeResetCursors(summary);let pages=0,added=0;while(pages++<8){const response=await request("POST","/system/diagnostics/changes",{AfterSequence:mfeDiagnostics.changeCursor,MaxEntries:750});const entries=Array.isArray(response.Value)?response.Value:[];if(!entries.length)break;for(const item of entries){const seq=Number(item.Sequence||0);if(seq>mfeDiagnostics.changeCursor)mfeDiagnostics.changeCursor=seq;mfeDiagnostics.changes.unshift(item);}added+=entries.length;if(entries.length<750)break;}if(mfeDiagnostics.changes.length>5000)mfeDiagnostics.changes.length=5000;if(added)mfeRenderChanges();mfeDiagnostics.available=true;mfeRenderDiagnosticStatus();}catch(error){mfeDiagnosticFailure(error);}finally{mfeDiagnostics.changeBusy=false;}}
function mfeRenderCodecReservations(){
  const grid=$("#codec-reservation-grid"),updated=$("#codec-reservation-updated");
  if(!grid)return;
  const codecNames=["NIPR VTC 1","NIPR VTC 2","NIPR VTC 3","NIPR VTC 4"];
  const records=(mfeDiagnostics.currentState||[]).filter(x=>clean(x.Control).toLowerCase()==="vtc farm reservation");
  const byCodec=new Map();
  records.forEach(x=>{const name=clean(x.Device);if(!name)return;if(!byCodec.has(name))byCodec.set(name,{});byCodec.get(name)[clean(x.Property)]=x.Value;});
  grid.innerHTML=codecNames.map((name,index)=>{
    const values=byCodec.get(name);
    if(!values){return `<article class="codec-reservation-card is-waiting"><div class="codec-reservation-head"><div><span>CODEC ${index+1}</span><h3>${escapeHtml(name)}</h3></div><span class="codec-reservation-pill waiting">Waiting</span></div><div class="codec-reservation-room"><small>Assignment</small><strong>Runtime state not received</strong></div></article>`;}
    const assigned=clean(values.ReservationState).toLowerCase()==="assigned";
    const room=clean(values.AssignedRoom);
    const hw=values.HardwareAvailable===true||String(values.HardwareAvailable).toLowerCase()==="true"||Number(values.HardwareAvailable)===1;
    const primary=clean(values.PrimaryEncoderUrl),secondary=clean(values.SecondaryEncoderUrl);
    return `<article class="codec-reservation-card ${assigned?'is-assigned':'is-unassigned'}"><div class="codec-reservation-head"><div><span>CODEC ${index+1}</span><h3>${escapeHtml(name)}</h3></div><span class="codec-reservation-pill ${assigned?'assigned':'available'}">${assigned?'Assigned':'Unassigned'}</span></div><div class="codec-reservation-room"><small>${assigned?'Assigned room':'Status'}</small><strong>${escapeHtml(assigned?(room||'Room name unavailable'):'Available for reservation')}</strong></div><div class="codec-reservation-meta"><span><i class="status-dot ${hw?'status-good':'status-warn'}"></i>${hw?'Codec hardware available':'Codec hardware unavailable'}</span></div><details class="codec-reservation-streams"><summary>Encoder streams</summary><div><small>Primary</small><code>${escapeHtml(primary||'Not learned yet')}</code></div><div><small>Secondary</small><code>${escapeHtml(secondary||'Not learned yet')}</code></div></details></article>`;
  }).join('');
  if(updated){const latest=records.reduce((max,x)=>{const d=x.Timestamp?new Date(x.Timestamp):null;return d&&!Number.isNaN(+d)&&(!max||d>max)?d:max;},null);updated.textContent=latest?`Updated ${nowTimeFor(latest)}`:'Waiting for runtime state';}
}

function farmMediaCell(media,label){
  media=media||{};
  const stream=clean(media.Stream),desired=clean(media.Desired),observed=clean(media.Observed),match=media.Match===true||String(media.Match).toLowerCase()==="true";
  let routeClass="wait",routeText="Waiting";
  if(desired){routeClass=match?"good":"bad";routeText=match?"Applied":"Check";}
  const detail=desired?`Desired: ${desired}${observed?`\nObserved: ${observed}`:""}`:(stream?`Stream: ${stream}`:"No stream received");
  return `<div class="farm-media-row"><div><small>JOIN ${Number(media.Join||0)}</small><strong>${escapeHtml(label)}</strong></div><code title="${escapeAttr(detail)}">${escapeHtml(stream||desired||"Not populated")}</code><span class="farm-route-match ${routeClass}">${routeText}</span></div>`;
}


const FIELD_STEPS=[
  {id:"baseline",label:"Baseline ready",desc:"EISC registered and room is available before the request."},
  {id:"request",label:"Reserve request observed",desc:"Farm sees the legacy room begin the VTC reservation sequence."},
  {id:"assigned",label:"Codec assigned",desc:"A Farm codec is assigned to the selected room."},
  {id:"camera",label:"Camera stream received",desc:"Legacy serial join 6 is populated."},
  {id:"presentation",label:"Presentation stream received",desc:"Legacy serial join 7 is populated."},
  {id:"audio",label:"Audio stream received",desc:"Legacy serial join 8 is populated."},
  {id:"routes",label:"Routes applied",desc:"Desired media routes agree with the observed/applied decoder state."},
  {id:"release",label:"Release observed",desc:"Farm sees the room enter release or the assignment clear after being active."},
  {id:"idle",label:"Returned to idle",desc:"Codec is unassigned and the room returns to a stable idle state."}
];
function truthy(v){return v===true||String(v).toLowerCase()==="true"||Number(v)===1;}
function fieldRoom(){const rooms=Array.isArray((mfeDiagnostics.farmStatus||{}).Rooms)?mfeDiagnostics.farmStatus.Rooms:[];const room=fieldSession.room||clean($('#farm-field-room')?.value||'');return rooms.find(r=>clean(r.Name)===room)||null;}
function fieldMediaReady(m){return !!clean((m||{}).Stream);}
function fieldRouteGood(m){m=m||{};const desired=clean(m.Desired),observed=clean(m.Observed);if(!desired)return false;return truthy(m.Match)||desired===observed;}
function fieldSnapshot(room){
  room=room||fieldRoom(); if(!room)return null;
  const one=m=>({stream:clean((m||{}).Stream),desired:clean((m||{}).Desired),observed:clean((m||{}).Observed),match:truthy((m||{}).Match)});
  return {captured:new Date().toISOString(),room:clean(room.Name),state:clean(room.State)||'Unknown',eisc:room.EiscRegistered!==false,assigned:truthy(room.CodecAssigned),codec:clean(room.Codec),camera:one(room.Camera),presentation:one(room.Presentation),audio:one(room.Audio)};
}
function fieldKnownGoodKey(room){return 'mav-vtc-farm-known-good:'+clean(room||fieldSession.room||'').toLowerCase();}
function fieldLoadKnownGood(room){try{const raw=localStorage.getItem(fieldKnownGoodKey(room));return raw?JSON.parse(raw):null;}catch(_){return null;}}
function fieldSaveKnownGood(){if(fieldSession.result!=='PASS'||!fieldSession.room)return;const durations={};FIELD_STEPS.forEach(x=>{const p=fieldSession.steps[x.id];if(p&&fieldSession.start)durations[x.id]=Math.max(0,(+p.time-+fieldSession.start)/1000);});const good={saved:new Date().toISOString(),room:fieldSession.room,durations,summary:fieldSummaryText()};try{localStorage.setItem(fieldKnownGoodKey(fieldSession.room),JSON.stringify(good));fieldRecord('reference','Known-good pattern saved',fieldSession.room);log('info','FIELD TEST: known-good pattern saved',fieldSession.room);}catch(_){ }renderFarmFieldDiagnostics();}
function fieldCaptureBaseline(){const snap=fieldSnapshot();if(!snap)return;fieldSession.baseline=snap;fieldRecord('baseline','Technician captured baseline',`state=${snap.state}; codec=${snap.codec||'none'}`);log('info','FIELD TEST: baseline captured',fieldSession.room);renderFarmFieldDiagnostics();}
function fieldChangesFromBaseline(room){const b=fieldSession.baseline,c=fieldSnapshot(room);if(!b||!c)return [];const out=[];const add=(label,a,z)=>{if(String(a)!==String(z))out.push(`${label}: ${a||'—'} → ${z||'—'}`);};add('State',b.state,c.state);add('Codec',b.codec,c.codec);add('Assigned',b.assigned,c.assigned);add('Camera',b.camera.stream,c.camera.stream);add('Presentation',b.presentation.stream,c.presentation.stream);add('Audio',b.audio.stream,c.audio.stream);add('Camera route',b.camera.observed,c.camera.observed);add('Presentation route',b.presentation.observed,c.presentation.observed);add('Audio route',b.audio.observed,c.audio.observed);return out;}
function fieldLastMarker(text){return [...fieldSession.markers].reverse().find(x=>x.label.includes(text))||null;}
function fieldRecord(type,text,detail){if(!fieldSession.active&&!fieldSession.start)return;const item={time:new Date(),type, text:clean(text), detail:clean(detail)};fieldSession.history.push(item);if(fieldSession.history.length>400)fieldSession.history.shift();}
function fieldMark(label){if(!fieldSession.active)return;const item={time:new Date(),label};fieldSession.markers.push(item);fieldRecord('technician',label,'Browser-local technician marker; no command was sent.');log('info',`FIELD TEST MARKER: ${label}`,fieldSession.room||'No room selected');renderFarmFieldDiagnostics();}
function fieldPass(id,detail){if(fieldSession.steps[id])return;fieldSession.steps[id]={passed:true,time:new Date(),detail:clean(detail)};fieldRecord('pass',FIELD_STEPS.find(x=>x.id===id)?.label||id,detail);}
function fieldHasReleaseEvidence(room){if(fieldSession.steps.release)return true;const st=clean(room?.State).toLowerCase();if(st.includes('releas'))return true;const start=fieldSession.start?+fieldSession.start:0;return state.logs.some(x=>{const t=+new Date(x.time),txt=(x.message+' '+x.detail).toLowerCase();return t>=start&&(!fieldSession.room||txt.includes(fieldSession.room.toLowerCase()))&&(txt.includes('release')||txt.includes('join=[2]')||txt.includes('join 2'));});}
function evaluateFieldWorkflow(){
  const room=fieldRoom();
  if(!fieldSession.active&&!fieldSession.start)return {room,stop:false,title:'No stop condition detected',detail:'Waiting for an active test session.',action:'Select one legacy room and start a test session.',actionDetail:'The workflow will evaluate the Farm state automatically as the technician performs the physical room actions.',result:'NOT STARTED'};
  if(!room)return {room:null,stop:true,title:'NEEDS ENGINEERING REVIEW — room telemetry unavailable',detail:'The selected room is not present in the Farm status snapshot.',action:'Stop this test and export the evidence package.',actionDetail:'Do not continue until the selected room is visible in the read-only Farm status.',result:'STOPPED'};
  const st=clean(room.State)||'Unknown',stl=st.toLowerCase(),assigned=truthy(room.CodecAssigned),eisc=room.EiscRegistered!==false;
  const camera=room.Camera||{},presentation=room.Presentation||{},audio=room.Audio||{};
  const anyBadRoute=[camera,presentation,audio].some(m=>clean(m.Desired)&&!fieldRouteGood(m));
  let stop=false,title='No stop condition detected',detail='Continue the guided sequence.';
  if(!state.cwsOnline){stop=true;title='NEEDS ENGINEERING REVIEW — Farm telemetry offline';detail='The monitor lost CWS visibility during the active test.';}
  else if(!eisc){stop=true;title='NEEDS ENGINEERING REVIEW — EISC registration failed';detail=clean(room.EiscRegistrationFailure)||'The selected room EISC is not registered.';}
  else if(/error|failed|fault|exception/i.test(st)){stop=true;title=`NEEDS ENGINEERING REVIEW — room state ${st}`;detail=clean(room.Detail)||'The Farm reported a fault state.';}
  else if(assigned&&!clean(room.Codec)){stop=true;title='NEEDS ENGINEERING REVIEW — assignment state inconsistent';detail='The room reports CodecAssigned but no codec identity was returned.';}
  else if(anyBadRoute&&fieldSession.steps.assigned){stop=true;title='NEEDS ENGINEERING REVIEW — route mismatch';detail='At least one desired decoder route does not match the observed/applied route.';}
  if(stop&&!fieldSession.stopReason){fieldSession.stopReason=title;fieldRecord('stop',title,detail);}

  if(eisc&&!assigned&&(stl==='idle'||stl.includes('wait')||stl.includes('ready')||stl.includes('unassigned')))fieldPass('baseline',`EISC registered; state=${st}; codec unassigned.`);
  if(/reservationrequested|reserve|request/i.test(st)&&!/release/i.test(st))fieldPass('request',`Farm state=${st}.`);
  if(assigned){fieldPass('request',`Assignment proves a reserve sequence reached the Farm; state=${st}.`);fieldPass('assigned',`Assigned codec ${clean(room.Codec)||'unknown'}.`);}
  if(fieldMediaReady(camera))fieldPass('camera',`Join ${camera.Join||6}=${clean(camera.Stream)}.`);
  if(fieldMediaReady(presentation))fieldPass('presentation',`Join ${presentation.Join||7}=${clean(presentation.Stream)}.`);
  if(fieldMediaReady(audio))fieldPass('audio',`Join ${audio.Join||8}=${clean(audio.Stream)}.`);
  if(fieldSession.steps.assigned&&fieldRouteGood(camera)&&fieldRouteGood(presentation)&&fieldRouteGood(audio))fieldPass('routes','Camera, Presentation, and Audio desired routes match observed/applied state.');
  if(fieldHasReleaseEvidence(room))fieldPass('release',`Release evidence observed; current state=${st}.`);
  if(fieldSession.steps.release&&!assigned&&(stl==='idle'||stl.includes('unassigned')||stl.includes('wait')))fieldPass('idle',`Room returned to ${st} with no codec assigned.`);

  const all=FIELD_STEPS.every(x=>fieldSession.steps[x.id]);
  fieldSession.result=stop?'STOPPED':all?'PASS':fieldSession.active?'IN PROGRESS':'INCOMPLETE';
  let action='Continue observing the current test step.',actionDetail='Use the workflow below; completed steps remain latched for this browser session.';
  if(stop){action='STOP TEST — export evidence and export the evidence package for engineering review.';actionDetail=detail;}
  else if(fieldSession.active&&fieldSession.roomEvidence==='unknown'){action='Check the legacy room for its own program log now.';actionDetail='If a room log exists, select “Program log available.” Otherwise select Debugger/Test Manager or No room-side logging found. This does not block Farm telemetry capture.';}
  else if(all){action='Test sequence complete. End the session and export both files.';actionDetail='The full reserve → media → route → release → idle sequence was observed.';}
  else {
    const next=FIELD_STEPS.find(x=>!fieldSession.steps[x.id]);
    if(next){
      const guidance={baseline:['Return the room to its normal idle state.','Confirm the selected room is idle/unassigned before requesting VTC.'],request:['Request VTC service in the legacy room, then click “Mark VTC request now”.','The Farm should observe a Reserve sequence (digital join 1) and move toward codec assignment.'],assigned:['Wait for the Farm to assign a codec.','Do not manually force routing; let the legacy EISC workflow proceed.'],camera:['Wait for Camera join 6.','If it does not populate, capture the legacy room log/debugger state before changing anything.'],presentation:['Wait for Presentation join 7.','The workflow will latch it as soon as the Farm receives it.'],audio:['Wait for Audio join 8.','The workflow will latch it as soon as the Farm receives it.'],routes:['Wait for all desired routes to match observed routes.','A mismatch becomes a STOP / NEEDS ENGINEERING REVIEW condition.'],release:['Release VTC service in the legacy room, then click “Mark release now”.','The Farm should observe the release and clear the codec assignment.'],idle:['Wait for the room to return to idle/unassigned.','Once idle is observed, end the session and export the evidence.']}[next.id];
      action=guidance[0];actionDetail=guidance[1];
    }
  }
  const snap=JSON.stringify({state:st,assigned,codec:clean(room.Codec),eisc,c:clean(camera.Stream),p:clean(presentation.Stream),a:clean(audio.Stream),cm:truthy(camera.Match),pm:truthy(presentation.Match),am:truthy(audio.Match)});
  if(fieldSession.active&&snap!==fieldSession.lastSnapshot){fieldSession.lastSnapshot=snap;fieldRecord('state',`Room state changed: ${st}`,`codec=${clean(room.Codec)||'none'}; joins 6/7/8=${fieldMediaReady(camera)?'Y':'N'}/${fieldMediaReady(presentation)?'Y':'N'}/${fieldMediaReady(audio)?'Y':'N'}`);}
  return {room,stop,title,detail,action,actionDetail,result:fieldSession.result};
}
function fieldAdvisor(assessment){
  const room=assessment.room,seeing={title:'No active room test.',detail:'Select a room and start a session.'},why={title:'Waiting for evidence',detail:'No diagnostic conclusion has been reached yet.'},dont={title:'Nothing blocked',detail:'The advisor will identify downstream systems that should not be investigated yet.'};
  if(!room)return {seeing,why,dont,confidence:'WAITING'};
  const st=clean(room.State)||'Unknown',assigned=truthy(room.CodecAssigned),cam=fieldMediaReady(room.Camera),pres=fieldMediaReady(room.Presentation),aud=fieldMediaReady(room.Audio);
  seeing.title=`${fieldSession.room}: ${st}${assigned?` · Codec ${clean(room.Codec)||'assigned'}`:' · no codec assigned'}`;
  seeing.detail=`EISC ${room.EiscRegistered===false?'NOT REGISTERED':'registered'} · joins 6/7/8 ${cam?'Y':'N'}/${pres?'Y':'N'}/${aud?'Y':'N'} · workflow ${fieldSession.result}.`;
  if(assessment.stop){why.title='A stop condition is supported by current telemetry.';why.detail=assessment.detail;dont.title='Do not continue normal room testing.';dont.detail='Preserve the current state and export the evidence package before changing anything.';return {seeing,why,dont,confidence:'STOP'};}
  if(!fieldSession.steps.request){why.title='The Farm has not yet observed a reservation sequence.';why.detail='EISC may be online, but codec allocation cannot begin until the legacy room generates the Reserve request.';dont.title='Do not troubleshoot codecs or NVX routing yet.';dont.detail='Those layers have not been reached until Reserve is observed.';}
  else if(!fieldSession.steps.assigned){why.title='Reserve activity reached the Farm, but no codec is assigned yet.';why.detail='Stay at the allocation layer until the Farm reports a codec identity.';dont.title='Do not chase media joins yet.';dont.detail='Joins 6/7/8 are downstream of successful allocation.';}
  else if(!(cam&&pres&&aud)){const missing=[!cam&&'Camera join 6',!pres&&'Presentation join 7',!aud&&'Audio join 8'].filter(Boolean).join(', ');why.title=`Codec assignment succeeded; waiting on ${missing}.`;why.detail='The compatibility path intentionally holds the codec while late legacy serial joins arrive.';dont.title='Do not force a re-reservation.';dont.detail='The Farm should complete each route independently when the corresponding serial join arrives.';}
  else if(!fieldSession.steps.routes){why.title='All room media streams arrived; route confirmation is now the active layer.';why.detail='Compare desired decoder URLs with observed/applied URLs before touching the room program.';dont.title='Do not change EISC mappings yet.';dont.detail='The room has already supplied its media values.';}
  else if(!fieldSession.steps.release){why.title='Reservation and routing succeeded.';why.detail='The remaining proof is a clean legacy Release and return to an unassigned state.';dont.title='Do not change routing.';dont.detail='The active media path has already passed.';}
  else {why.title='The transaction has reached the release/idle verification stage.';why.detail='Confirm the codec clears and the room stabilizes before closing the test.';dont.title='Do not start another test yet.';dont.detail='Finish and export this evidence window first.';}
  const request=fieldLastMarker('VTC request');
  if(fieldSession.active&&request&&!fieldSession.steps.request){const sec=(Date.now()-+request.time)/1000;if(sec>=30){seeing.detail+=` Reserve has been absent for ${Math.floor(sec)}s after the technician marker.`;why.title='Reserve is now materially late.';why.detail='The technician marked a physical VTC request, but the Farm still has no Reserve evidence. Check room-side join 1/logging now.';}else if(sec>=10){seeing.detail+=` Waiting ${Math.floor(sec)}s for Reserve.`;}}
  return {seeing,why,dont,confidence:fieldSession.active?'LIVE':'REVIEW'};
}
function renderFieldWorkflow(assessment){
  const steps=$('#farm-workflow-steps'),result=$('#farm-workflow-result'),timeline=$('#farm-session-timeline'),count=$('#farm-timeline-count'),action=$('#farm-guidance-action'),actionDetail=$('#farm-guidance-detail'),esc=$('#farm-escalation-card'),escTitle=$('#farm-escalation-title'),escDetail=$('#farm-escalation-detail');
  const advisor=fieldAdvisor(assessment),set=(id,v)=>{const el=$(id);if(el)el.textContent=v;};
  if(action)action.textContent=assessment.action;if(actionDetail)actionDetail.textContent=assessment.actionDetail;
  set('#farm-advisor-seeing',advisor.seeing.title);set('#farm-advisor-seeing-detail',advisor.seeing.detail);set('#farm-advisor-why',advisor.why.title);set('#farm-advisor-why-detail',advisor.why.detail);set('#farm-advisor-dont',advisor.dont.title);set('#farm-advisor-dont-detail',advisor.dont.detail);set('#farm-advisor-confidence',advisor.confidence);
  const changes=fieldChangesFromBaseline(assessment.room);const ch=$('#farm-advisor-changes');if(ch)ch.innerHTML=fieldSession.baseline?(changes.length?changes.map(x=>`<span>${escapeHtml(x)}</span>`).join(''):'<span>No observable state differences from captured baseline.</span>'):'<span>No baseline captured.</span>';
  const bs=$('#farm-advisor-baseline-state');if(bs)bs.textContent=fieldSession.baseline?`Baseline ${nowTimeFor(new Date(fieldSession.baseline.captured))} · ${fieldSession.baseline.state}`:'No baseline captured';
  const good=fieldLoadKnownGood(fieldSession.room),kg=$('#farm-advisor-known-good');if(kg){if(!good)kg.innerHTML='<span>No known-good pattern saved for this room.</span>';else {const current=FIELD_STEPS.filter(x=>fieldSession.steps[x.id]).length;const refs=FIELD_STEPS.filter(x=>good.durations&&good.durations[x.id]!=null).slice(0,5).map(x=>`${x.label}: ${good.durations[x.id].toFixed(1)}s`);kg.innerHTML=`<span>Saved ${escapeHtml(new Date(good.saved).toLocaleString())}</span><span>Current progress: ${current}/${FIELD_STEPS.length} steps</span>${refs.map(x=>`<span>${escapeHtml(x)}</span>`).join('')}`;}}
  const sb=$('#farm-field-save-good');if(sb)sb.disabled=fieldSession.result!=='PASS';
  if(esc){esc.classList.toggle('stop',assessment.stop);esc.classList.toggle('clear',!assessment.stop&&fieldSession.active);}if(escTitle)escTitle.textContent=assessment.title;if(escDetail)escDetail.textContent=assessment.detail;
  if(result){result.textContent=assessment.result;result.className='farm-workflow-result '+(assessment.result==='PASS'?'pass':assessment.result==='STOPPED'?'stop':assessment.result==='IN PROGRESS'?'progress':'waiting');}
  if(steps)steps.innerHTML=FIELD_STEPS.map((x,i)=>{const pass=fieldSession.steps[x.id],current=!pass&&FIELD_STEPS.slice(0,i).every(y=>fieldSession.steps[y.id]);return `<div class="farm-workflow-step ${pass?'passed':current&&fieldSession.active?'current':'pending'}"><span class="farm-step-index">${pass?'✓':i+1}</span><div><strong>${escapeHtml(x.label)}</strong><p>${escapeHtml(pass?(pass.detail||x.desc):x.desc)}</p>${pass?`<small>${escapeHtml(nowTimeFor(pass.time))}</small>`:''}</div></div>`;}).join('');
  const events=[...fieldSession.history].sort((a,b)=>+b.time-+a.time);
  if(count)count.textContent=`${events.length} events`;
  if(timeline)timeline.innerHTML=events.length?events.slice(0,80).map(x=>`<div class="farm-timeline-event ${escapeAttr(x.type)}"><time>${escapeHtml(nowTimeFor(x.time))}</time><div><strong>${escapeHtml(x.text)}</strong>${x.detail?`<p>${escapeHtml(x.detail)}</p>`:''}</div></div>`).join(''):'<div class="empty-state">Start a test session to capture technician markers and state transitions.</div>';
}
function fieldSummaryText(){
  const a=evaluateFieldWorkflow(),room=fieldSession.room||'No room selected';
  const lines=[`MAV VTC Farm Field Test Summary`,`Room: ${room}`,`Legacy room evidence: ${fieldSession.roomEvidence}`,`Result: ${a.result}`,`Started: ${fieldSession.start?fieldSession.start.toISOString():'Not started'}`,`Ended: ${fieldSession.end?fieldSession.end.toISOString():(fieldSession.active?'ACTIVE':'Not ended')}`,`Stop condition: ${fieldSession.stopReason||'None'}`,'','Validation sequence:'];
  FIELD_STEPS.forEach((x,i)=>{const p=fieldSession.steps[x.id];lines.push(`${p?'PASS':'----'} ${i+1}. ${x.label}${p?` @ ${p.time.toISOString()} — ${p.detail}`:''}`);});
  lines.push('','Technician markers:');
  if(fieldSession.markers.length)fieldSession.markers.forEach(m=>lines.push(`${m.time.toISOString()} — ${m.label}`));else lines.push('None');
  const adv=fieldAdvisor(a);lines.push('','Field Advisor:',adv.seeing.title,adv.seeing.detail,'','Current recommendation:',a.action,a.actionDetail,'','Why:',adv.why.title,adv.why.detail,'','Do not troubleshoot yet:',adv.dont.title,adv.dont.detail,'','NOTE: Browser workflow is read-only. Technician marker buttons do not send commands to the Farm or legacy room.');
  return lines.join('\r\n');
}
function downloadFieldSummary(){const blob=new Blob([fieldSummaryText()],{type:'text/plain'}),a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=`MAV-VTC-Farm-Test-Summary-${(fieldSession.room||'No-Room').replace(/[^a-z0-9_-]+/gi,'_')}-${new Date().toISOString().replace(/[:.]/g,'-')}.txt`;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);}

function renderFarmFieldDiagnostics(){
  const host=$("#farm-room-diagnostics"),updated=$("#farm-field-updated"),select=$("#farm-field-room"),session=$("#farm-field-session");
  if(!host)return;
  const status=mfeDiagnostics.farmStatus||{},rooms=Array.isArray(status.Rooms)?status.Rooms:[];
  if(select){
    const current=select.value;
    const names=rooms.map(r=>clean(r.Name)).filter(Boolean).sort();
    select.innerHTML='<option value="">All rooms</option>'+names.map(n=>`<option value="${escapeAttr(n)}">${escapeHtml(n)}</option>`).join('');
    if(names.includes(current))select.value=current;
  }
  const selected=select?clean(select.value):"";
  let rows=rooms.filter(r=>!selected||clean(r.Name)===selected);
  rows.sort((a,b)=>{
    const score=r=>(r.CodecAssigned?0:1)+(r.EiscRegistered===false?-2:0)+(clean(r.State)==="Idle"?2:0);
    return score(a)-score(b)||clean(a.Name).localeCompare(clean(b.Name));
  });
  // Preserve each room bubble's expansion state across the 3-second telemetry refresh.
  $$('details[data-farm-room]',host).forEach(d=>{const name=clean(d.dataset.farmRoom);if(name){farmKnownRooms.add(name);if(d.open)farmExpandedRooms.add(name);else farmExpandedRooms.delete(name);}});
  host.innerHTML=rows.map(r=>{
    const roomName=clean(r.Name)||'Unnamed room',stateName=clean(r.State)||"Unknown",assigned=!!r.CodecAssigned,eisc=r.EiscRegistered!==false;
    const fault=!eisc||/error|failed|nocodec/i.test(stateName);
    const known=farmKnownRooms.has(roomName);
    const shouldOpen=farmExpandedRooms.has(roomName)||(!known&&!!selected);
    farmKnownRooms.add(roomName);
    if(shouldOpen)farmExpandedRooms.add(roomName);
    return `<details class="farm-room-bubble ${assigned?'is-active':''} ${fault?'is-fault':''}" data-farm-room="${escapeAttr(roomName)}" data-state="${escapeAttr(stateName)}" ${shouldOpen?'open':''}><summary class="farm-room-bubble-summary"><div><span class="farm-room-name">${escapeHtml(roomName)}</span><small>${eisc?'EISC registered':'EISC registration failed'} · ${assigned?`Codec ${escapeHtml(r.Codec||'assigned')}`:'No codec assigned'}</small></div><span class="farm-room-state">${escapeHtml(stateName)}</span></summary><div class="farm-room-card-body"><div class="farm-room-detail">${escapeHtml(r.Detail||'No diagnostic detail')}</div><div class="farm-room-summary"><div><small>EISC</small><strong>${eisc?'Registered':'Failed'}</strong></div><div><small>Codec</small><strong>${escapeHtml(r.Codec|| (assigned?'Assigned':'None'))}</strong></div><div><small>Last event</small><strong>${r.LastEventUtc?escapeHtml(nowTimeFor(new Date(r.LastEventUtc))):'—'}</strong></div></div>${fault&&r.EiscRegistrationFailure?`<div class="farm-room-detail">Registration: ${escapeHtml(r.EiscRegistrationFailure)}</div>`:''}<div class="farm-media-grid">${farmMediaCell(r.Camera,'Camera')}${farmMediaCell(r.Presentation,'Presentation')}${farmMediaCell(r.Audio,'Audio')}</div></div></details>`;
  }).join('')||'<div class="empty-state">No room diagnostic records were returned by the Farm.</div>';
  $$('details[data-farm-room]',host).forEach(d=>d.addEventListener('toggle',()=>{const name=clean(d.dataset.farmRoom);if(!name)return;farmKnownRooms.add(name);if(d.open)farmExpandedRooms.add(name);else farmExpandedRooms.delete(name);}));
  if(updated)updated.textContent=mfeDiagnostics.lastSync?`Updated ${nowTimeFor(mfeDiagnostics.lastSync)}`:'Waiting for Farm status';
  const testSelected=$('#farm-test-selected-room');if(testSelected)testSelected.textContent=selected||'All rooms';
  const roomMeta=$('#farm-rooms-collapse-meta');if(roomMeta)roomMeta.textContent=selected?`${selected} · 1 room shown`:`${rooms.length} rooms · ${rows.filter(r=>r.CodecAssigned).length} assigned`;
  if(session){
    if(fieldSession.active){session.classList.add('active');session.textContent=`LOCAL TEST ACTIVE · ${fieldSession.room||'No room'} · started ${nowTimeFor(fieldSession.start)}`;}
    else{session.classList.remove('active');session.textContent=fieldSession.end?`Last local test ended ${nowTimeFor(fieldSession.end)}`:'No local test session active';}
  }
  const assessment=evaluateFieldWorkflow();
  const testMeta=$('#farm-test-collapse-meta');if(testMeta)testMeta.textContent=fieldSession.active?`${fieldSession.room} · ${assessment.result}`:fieldSession.end?`${fieldSession.room||'Last room'} · ${assessment.result}`:'No test active';
  renderFieldWorkflow(assessment);
}

async function mfePollFarmStatus(){
  if(mfeDiagnostics.farmStatusBusy||!state.cwsOnline)return;
  mfeDiagnostics.farmStatusBusy=true;
  try{
    const response=await request("GET","/system/vtcfarm/status");
    if(!response||!response.Value)throw new Error("Farm status response missing Value");
    mfeDiagnostics.farmStatus=response.Value;
    mfeDiagnostics.lastSync=new Date();
    renderFarmFieldDiagnostics();
  }catch(error){
    const host=$("#farm-room-diagnostics");
    if(host)host.innerHTML=`<div class="empty-state">Read-only Farm room diagnostics are not available in this program build yet. ${escapeHtml(error.message||String(error))}</div>`;
  }finally{mfeDiagnostics.farmStatusBusy=false;}
}

function fieldEvidencePayload(){
  const start=fieldSession.start?+fieldSession.start:0,end=fieldSession.end?+fieldSession.end:Date.now(),room=fieldSession.room;
  const logs=state.logs.filter(x=>{const t=+new Date(x.time);return (!start||t>=start)&&t<=end&&(!room||(x.message+" "+x.detail).includes(room));}).map(x=>({Timestamp:new Date(x.time).toISOString(),Level:MFE_LEVELS[x.levelNumber==null?mfeLevelNumber(x.level):x.levelNumber],Source:x.source,Sequence:x.sequence||0,Message:x.message,Detail:x.detail||""}));
  return {Exported:new Date().toISOString(),HumanReadableSummary:fieldSummaryText(),Baseline:fieldSession.baseline,KnownGoodReference:fieldLoadKnownGood(room),Session:{Room:room||"No room selected",LegacyRoomEvidence:fieldSession.roomEvidence,Started:fieldSession.start?fieldSession.start.toISOString():null,Ended:fieldSession.end?fieldSession.end.toISOString():null,Active:fieldSession.active,Result:fieldSession.result,StopReason:fieldSession.stopReason||null},Workflow:{Steps:FIELD_STEPS.map(x=>({Id:x.id,Label:x.label,Passed:!!fieldSession.steps[x.id],Timestamp:fieldSession.steps[x.id]?.time?.toISOString()||null,Detail:fieldSession.steps[x.id]?.detail||null})),Markers:fieldSession.markers.map(x=>({Timestamp:x.time.toISOString(),Label:x.label})),Timeline:fieldSession.history.map(x=>({Timestamp:x.time.toISOString(),Type:x.type,Text:x.text,Detail:x.detail}))},FarmStatus:mfeDiagnostics.farmStatus,DiagnosticsSummary:mfeDiagnostics.summary,CurrentState:mfeDiagnostics.currentState,Changes:mfeDiagnostics.changes,Logs:logs};
}

function downloadFieldEvidence(){
  const payload=fieldEvidencePayload(),blob=new Blob([JSON.stringify(payload,null,2)],{type:'application/json'}),a=document.createElement('a');
  a.href=URL.createObjectURL(blob);a.download=`MAV-VTC-Farm-Field-Test-${(fieldSession.room||'All-Rooms').replace(/[^a-z0-9_-]+/gi,'_')}-${new Date().toISOString().replace(/[:.]/g,'-')}.json`;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);
}

async function mfePollState(){if(mfeDiagnostics.stateBusy||!state.cwsOnline)return;mfeDiagnostics.stateBusy=true;try{const response=await request("POST","/system/diagnostics/state",{});if(!Array.isArray(response.Value))throw new Error("Unexpected current state response");mfeDiagnostics.currentState=response.Value;mfeDiagnostics.available=true;mfeRenderChanges();mfeRenderCodecReservations();mfeRenderDiagnosticStatus();}catch(error){mfeDiagnosticFailure(error);}finally{mfeDiagnostics.stateBusy=false;}}
function mfeStartDiagnostics(){if(mfeDiagnostics.logTimer)clearInterval(mfeDiagnostics.logTimer);if(mfeDiagnostics.changeTimer)clearInterval(mfeDiagnostics.changeTimer);if(mfeDiagnostics.stateTimer)clearInterval(mfeDiagnostics.stateTimer);if(mfeDiagnostics.farmStatusTimer)clearInterval(mfeDiagnostics.farmStatusTimer);mfePollLogs().then(()=>{mfePollChanges();mfePollState();mfePollFarmStatus();});mfeDiagnostics.logTimer=setInterval(mfePollLogs,4000);mfeDiagnostics.changeTimer=setInterval(mfePollChanges,4500);mfeDiagnostics.stateTimer=setInterval(mfePollState,10500);mfeDiagnostics.farmStatusTimer=setInterval(mfePollFarmStatus,3000);}
function mfeRenderDiagnosticStatus(){const html=`<span class="mfe-status ${mfeDiagnostics.available?"good":"bad"}">${mfeDiagnostics.available?"● SDK logger live":"○ SDK logger unavailable"}</span><span>Log cursor ${mfeDiagnostics.logCursor}</span><span>State cursor ${mfeDiagnostics.changeCursor}</span><span>${mfeDiagnostics.summary?escapeHtml(mfeDiagnostics.summary.CurrentLogPath||""):"Requires MAV SDK diagnostics"}</span>`;const a=$("#mfe-log-status"),b=$("#mfe-diagnostic-status");if(a)a.innerHTML=html;if(b)b.innerHTML=html;}
function mfeSearchable(item){return [item.Device,item.Control,item.Property,mfeBrief(item.Value),mfeBrief(item.PreviousValue)].join(" ").toLowerCase();}
function mfeRenderChanges(){const list=$("#mfe-state-list"),changes=$("#mfe-change-list");if(!list||!changes)return;const q=($("#mfe-state-search")?.value||"").trim().toLowerCase(),mode=$("#mfe-change-filter")?.value||"all";let rows=mfeDiagnostics.currentState.filter(x=>!q||mfeSearchable(x).includes(q));rows.sort((a,b)=>[a.Device,a.Control,a.Property].join("|").localeCompare([b.Device,b.Control,b.Property].join("|")));list.innerHTML=rows.slice(0,1000).map(x=>`<div class="mfe-change-card"><header><strong>${escapeHtml(x.Device||"—")}</strong><small>${escapeHtml(x.Control||"—")}</small></header><div class="mfe-state-label">${escapeHtml(x.Property||"value")}</div><code class="mfe-state-value">${escapeHtml(mfeBrief(x.Value))}</code></div>`).join("")||'<div class="empty-state">No runtime state has been retained yet.</div>';let journal=mfeDiagnostics.changes.filter(x=>!q||mfeSearchable(x).includes(q));if(mode==="recent")journal=journal.slice(0,30);changes.innerHTML=journal.slice(0,1000).map(x=>`<div class="mfe-change-card"><header><strong>${escapeHtml(x.Device||"—")} · ${escapeHtml(x.Control||"—")}</strong><small>#${Number(x.Sequence||0)}</small></header><div class="mfe-state-label">${escapeHtml(x.Property||"value")}</div><div class="mfe-change-values"><code class="old">${escapeHtml(mfeBrief(x.PreviousValue))}</code><span>→</span><code class="new">${escapeHtml(mfeBrief(x.Value))}</code></div></div>`).join("")||'<div class="empty-state">No matching state transitions retained.</div>';const sc=$("#mfe-state-count"),cc=$("#mfe-change-count");if(sc)sc.textContent=`${rows.length} values`;if(cc)cc.textContent=`${journal.length} changes`;}
function renderLogs(){const list=$("#log-list");if(!list)return;const q=state.logSearch.toLowerCase(),source=$("#mfe-log-source")?.value||"all",threshold=Number($("#mfe-log-threshold")?.value||7);const filtered=state.logs.filter(x=>{const n=x.levelNumber==null?mfeLevelNumber(x.level):x.levelNumber;return n<=threshold&&(source==="all"||x.source===source)&&(!q||(x.message+" "+x.detail).toLowerCase().includes(q))&&(+new Date(x.time)>mfeLogDisplayCutoff);});list.innerHTML=filtered.slice(0,1100).map(x=>{const n=x.levelNumber==null?mfeLevelNumber(x.level):x.levelNumber;return `<div class="log-row log-level-${n} log-${n<=1?"error":n===2?"warn":"info"}"><span class="log-level">${escapeHtml(MFE_LEVELS[n]||"Information")}</span><time>${nowTimeFor(x.time)}</time><span class="log-source">${x.source==="program"?"MAV #"+x.sequence:"BROWSER"}</span><div class="log-content"><strong>${escapeHtml(x.message)}</strong>${x.detail?`<span>${escapeHtml(x.detail)}</span>`:""}</div></div>`;}).join("")||'<div class="empty-state">No matching events in the retained view.</div>';const errors=state.logs.filter(x=>x.source==="program"&&mfeLevelNumber(x.level)<=1).length;const badge=$("#monitor-log-badge");badge.hidden=!errors;badge.textContent=errors;const meta=$("#mfe-log-meta");if(meta)meta.textContent=`${filtered.length} shown / ${state.logs.length} retained locally · SDK limit 5,000 entries · ${mfeDiagnostics.available?"Live MAV feed connected":"Program feed not yet confirmed"} · Filter does not change SDK logging`;mfeRenderDiagnosticStatus();}


function dataTable(headers,rows){return `<div class="data-row data-head">${headers.map(h=>`<strong>${escapeHtml(h)}</strong>`).join('')}</div>${rows.map(row=>`<div class="data-row">${row.map(v=>`<span title="${escapeAttr(String(v))}">${escapeHtml(String(v))}</span>`).join('')}</div>`).join('')}`;}
function escapeHtml(v){return clean(v).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
function escapeAttr(v){return escapeHtml(v);}

function renderAll(){refreshAlertModel();renderHeader();renderOverview();renderAlerts();renderDevices();renderRoutes();renderDsp();renderLogs();mfeRenderCodecReservations();renderFarmFieldDiagnostics();}

function switchTab(name){
  state.activeTab=name;
  $$('.monitor-tab').forEach(b=>b.classList.toggle('is-active',b.dataset.tab===name));
  $$('.monitor-view').forEach(v=>v.classList.toggle('is-active',v.dataset.view===name));
}

function bind(){
  $$('.monitor-tab').forEach(b=>b.addEventListener('click',()=>switchTab(b.dataset.tab)));
  $('#monitor-refresh').addEventListener('click',async()=>{log('info','Manual refresh requested');await heartbeat(false);if(state.cwsOnline){await registerSubscriptions();pollSubscriptions();}});
  $('#alert-filter-group').addEventListener('click',e=>{const b=e.target.closest('[data-alert-filter]');if(!b)return;$$('[data-alert-filter]',$('#alert-filter-group')).forEach(x=>x.classList.toggle('is-active',x===b));state.alertFilter=b.dataset.alertFilter;renderAlerts();});
  $('#alert-list').addEventListener('click',e=>{const b=e.target.closest('[data-alert-ack]');if(!b)return;const entry=state.alerts.get(b.dataset.alertAck);if(!entry)return;entry.acknowledged=!entry.acknowledged;log('info',`${entry.acknowledged?'Acknowledged':'Unacknowledged'} alert: ${entry.title}`,entry.device);renderAlerts();renderOverview();});
  $('#device-grid').addEventListener('click',e=>{const property=e.target.closest('[data-inspect-id]');if(property){openInspector(property.dataset.inspectId);return;}const button=e.target.closest('.device-card-summary');if(!button)return;const card=button.closest('[data-device-id]'),id=card&&card.dataset.deviceId;if(!id)return;if(state.expandedDevices.has(id))state.expandedDevices.delete(id);else state.expandedDevices.add(id);const filter=($('#device-filter-group').querySelector('.is-active')||{}).dataset?.deviceFilter||'All';paintDeviceGrid(filter);});
  $('#log-clear').addEventListener('click',()=>{mfeLogDisplayCutoff=Date.now();renderLogs();});
  $('#log-download').addEventListener('click',()=>{const text=state.logs.slice().reverse().map(x=>`${new Date(x.time).toISOString()} [${MFE_LEVELS[x.levelNumber==null?mfeLevelNumber(x.level):x.levelNumber]}] [${x.source||'browser'}${x.sequence?' #'+x.sequence:''}] ${x.message}${x.detail?' | '+x.detail:''}`).join('\r\n');const blob=new Blob([text],{type:'text/plain'});const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=`MAV-VTC-Farm-Program-Log-${new Date().toISOString().replace(/[:.]/g,'-')}.log`;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);});
  $$('.log-filter button').forEach(b=>b.addEventListener('click',()=>{$$('.log-filter button').forEach(x=>x.classList.toggle('is-active',x===b));state.logFilter=b.dataset.logFilter;renderLogs();}));
  $('#log-search').addEventListener('input',e=>{state.logSearch=e.target.value||'';renderLogs();});
  $('#mfe-log-threshold')?.addEventListener('change',renderLogs);
  $('#mfe-log-source')?.addEventListener('change',renderLogs);
  $('#mfe-state-search')?.addEventListener('input',mfeRenderChanges);
  $('#mfe-change-filter')?.addEventListener('change',mfeRenderChanges);
  $('#mfe-refresh-state')?.addEventListener('click',()=>{mfePollChanges();mfePollState();mfePollFarmStatus();});
  $('#farm-field-room')?.addEventListener('change',()=>{if(!fieldSession.active)renderFarmFieldDiagnostics();});
  $('#farm-room-evidence')?.addEventListener('change',e=>{const value=clean(e.target.value||'unknown');if(fieldSession.active){fieldSession.roomEvidence=value;fieldRecord('technician','Legacy room evidence method updated',value);log('info','FIELD TEST: legacy room evidence method',value);}renderFarmFieldDiagnostics();});
  $('#farm-field-start')?.addEventListener('click',()=>{const room=clean($('#farm-field-room')?.value||'');if(!room){log('warning','Field test not started','Select one legacy room first.');alert('Select one legacy room before starting the test.');return;}fieldSession.active=true;fieldSession.start=new Date();fieldSession.end=null;fieldSession.room=room;fieldSession.roomEvidence=clean($('#farm-room-evidence')?.value||'unknown');fieldSession.markers=[];fieldSession.steps={};fieldSession.history=[];fieldSession.lastSnapshot=null;fieldSession.stopReason='';fieldSession.result='IN PROGRESS';fieldSession.baseline=null;$('#farm-field-room').disabled=true;$('#farm-field-start').disabled=true;$('#farm-field-mark-request').disabled=false;$('#farm-field-mark-release').disabled=false;$('#farm-field-end').disabled=false;$('#farm-field-baseline').disabled=false;fieldRecord('session','Test session started',room);log('info','Local field-test evidence window started',room);renderFarmFieldDiagnostics();});
  $('#farm-field-mark-request')?.addEventListener('click',()=>fieldMark('TECHNICIAN: VTC request performed in room'));
  $('#farm-field-mark-release')?.addEventListener('click',()=>fieldMark('TECHNICIAN: VTC release performed in room'));
  $('#farm-field-end')?.addEventListener('click',()=>{fieldSession.active=false;fieldSession.end=new Date();fieldRecord('session','Test session ended',fieldSession.room);$('#farm-field-room').disabled=false;$('#farm-field-start').disabled=false;$('#farm-field-mark-request').disabled=true;$('#farm-field-mark-release').disabled=true;$('#farm-field-end').disabled=true;$('#farm-field-baseline').disabled=true;log('info','Local field-test evidence window ended',fieldSession.room);renderFarmFieldDiagnostics();});
  $('#farm-field-export')?.addEventListener('click',downloadFieldEvidence);
  $('#farm-field-summary')?.addEventListener('click',downloadFieldSummary);
  $('#farm-field-baseline')?.addEventListener('click',fieldCaptureBaseline);
  $('#farm-field-save-good')?.addEventListener('click',fieldSaveKnownGood);
  $('#farm-field-export')?.addEventListener('click',()=>{const payload=fieldEvidencePayload();payload.PackagePurpose='Export Complete Evidence';payload.Advisor=fieldAdvisor(evaluateFieldWorkflow());const blob=new Blob([JSON.stringify(payload,null,2)],{type:'application/json'}),a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=`PUBLIC-DEMO-EVIDENCE-${(fieldSession.room||'No-Room').replace(/[^a-z0-9_-]+/gi,'_')}-${new Date().toISOString().replace(/[:.]/g,'-')}.json`;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);});
  $('#mfe-export-evidence')?.addEventListener('click',()=>{const payload={Exported:new Date().toISOString(),Summary:mfeDiagnostics.summary,FarmStatus:mfeDiagnostics.farmStatus,CurrentState:mfeDiagnostics.currentState,Changes:mfeDiagnostics.changes};const blob=new Blob([JSON.stringify(payload,null,2)],{type:'application/json'});const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=`MAV-VTC-Farm-Diagnostics-${new Date().toISOString().replace(/[:.]/g,'-')}.json`;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);});
}

async function init(){
  bind();updateClock();setInterval(updateClock,1000);renderAll();
  const ok=await heartbeat(true);
  if(ok){await registerSubscriptions();startSubscriptionPolling();mfeStartDiagnostics();}
  startHeartbeat();
}

function wait(){if($('#mav-monitor')){init();return;}setTimeout(wait,100);} wait();
})();
