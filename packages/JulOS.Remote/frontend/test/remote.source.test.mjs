import assert from 'node:assert/strict';
import test from 'node:test';
import {
  createClipboardPipeline,
  createKeyboardPipeline,
  createPointerPipeline,
  createResizeScheduler,
  isKeyboardReleaseShortcut,
  resizeDisplay,
  resolveViewport,
  sendTextAsKeysyms,
  splitDisplayEndpoint,
  validateDisplayDescriptor,
  validateInteraction,
} from '../remote.source.js';

const origin='https://os.example.test';

test('display descriptor stays same-origin and token-free',()=>{
  const descriptor=validateDisplayDescriptor({kind:'graphical',contractVersion:'1.0.0',endpoint:'/api/v1/remote/sessions/11111111-1111-4111-8111-111111111111/display?package=de.juloc.julos.remote&revision=7&expires=1785873660',expiresAtUtc:'2026-08-04T21:01:00+00:00'});
  const endpoint=splitDisplayEndpoint(descriptor.endpoint,origin);
  assert.equal(endpoint.tunnelUrl,'/api/v1/remote/sessions/11111111-1111-4111-8111-111111111111/display');
  assert.equal(endpoint.connectData,'package=de.juloc.julos.remote&revision=7&expires=1785873660');
  assert.throws(()=>validateDisplayDescriptor({...descriptor,endpoint:'/display?access_token=secret'}));
});

function fakeElement(){const listeners=new Map();return{value:'',listeners,addEventListener(n,h,o){listeners.set(`${n}:${o??''}`,h);},removeEventListener(n,h,o){if(listeners.get(`${n}:${o??''}`)===h)listeners.delete(`${n}:${o??''}`);},remove(){},focus(){},blur(){}};}

test('Gboard paste and composition send text once',()=>{
  const events=[];const sink=fakeElement();
  class Keyboard{constructor(){this.onkeydown=null;this.onkeyup=null;this.resetCount=0;}reset(){this.resetCount++;}}
  class InputSink{getElement(){return sink;}focus(){}}
  const target=fakeElement();target.append=()=>{};
  const pipeline=createKeyboardPipeline({Keyboard,InputSink},target,{sendKeyEvent:(...a)=>events.push(a)},true);
  const paste=sink.listeners.get('paste:');
  paste({clipboardData:{getData:()=> 'Hallo 👋'},preventDefault(){}});
  const afterPaste=events.length;
  sink.value='Hallo 👋';sink.listeners.get('input:')({inputType:'insertFromPaste',data:'Hallo 👋'});
  assert.equal(events.length,afterPaste);
  sink.listeners.get('compositionstart:')({});
  pipeline.keyboard.onkeydown(65);pipeline.keyboard.onkeyup(65);
  assert.equal(events.length,afterPaste);
  sink.listeners.get('compositionend:')({data:'漢字'});
  assert.equal(events.length,afterPaste+4);
  pipeline.dispose();
});

test('keyboard release shortcut is preserved',()=>{
  const listeners=new Map();let blurred=0;let released=0;
  class Keyboard{constructor(){this.onkeydown=null;this.onkeyup=null;this.resetCount=0;}reset(){this.resetCount++;}}
  const target={addEventListener:(n,h,o)=>listeners.set(`${n}:${o}`,h),removeEventListener(){},blur(){blurred++;}};
  const pipeline=createKeyboardPipeline({Keyboard},target,{sendKeyEvent(){}},false,()=>released++);
  const ev={key:'Escape',ctrlKey:true,altKey:true,shiftKey:true,preventDefault(){},stopImmediatePropagation(){}};
  assert.equal(isKeyboardReleaseShortcut(ev),true);listeners.get('keydown:true')(ev);assert.equal(blurred,1);assert.equal(released,1);pipeline.dispose();
});

test('direct touch is default with long press and two-finger scroll thresholds',()=>{
  let touch=0,pad=0,mouse=0;
  class Mouse{constructor(){mouse++;this.currentState={x:4,y:5};}}
  Mouse.Touchscreen=class{constructor(){touch++;this.currentState={x:4,y:5};this.longPressThreshold=0;this.scrollThreshold=0;}};
  Mouse.Touchpad=class{constructor(){pad++;this.currentState={x:4,y:5};this.scrollThreshold=0;}};
  const sent=[];const pipeline=createPointerPipeline({Mouse},{},{sendMouseState:(...a)=>sent.push(a)},true,{touchMode:'direct',gestureRightClick:true,longPressMs:750,scrollThreshold:12});
  assert.equal(touch,1);assert.equal(pad,0);assert.equal(mouse,0);assert.equal(pipeline.pointer.longPressThreshold,750);assert.equal(pipeline.pointer.scrollThreshold,12);pipeline.clickRight();assert.equal(sent.length,2);
});

test('trackpad mode and gesture disable are configurable',()=>{
  class Mouse{constructor(){this.currentState={x:1,y:2};}}
  Mouse.Touchscreen=class extends Mouse{constructor(){super();this.longPressThreshold=0;this.scrollThreshold=0;}};
  Mouse.Touchpad=class extends Mouse{constructor(){super();this.scrollThreshold=0;}};
  const sent=[];const pipeline=createPointerPipeline({Mouse},{},{sendMouseState:(s)=>sent.push(s)},true,{touchMode:'touchpad',gestureRightClick:false,scrollThreshold:32});
  assert.equal(pipeline.pointer.scrollThreshold,32);pipeline.pointer.onmousedown({x:1,y:2,right:true});assert.equal(sent[0].right,false);pipeline.clickRight();assert.equal(sent.at(-2).right,true);
});

test('viewport and scaling presets work without forced resize',()=>{
  const stage={dataset:{},getBoundingClientRect:()=>({width:1000,height:700})};
  assert.deepEqual(resolveViewport(stage,{resolutionMode:'1920x1080'}),{width:1920,height:1080,deviceScaleFactor:1});
  assert.deepEqual(resolveViewport(stage,{resolutionMode:'custom',customWidth:3000,customHeight:1600}),{width:3000,height:1600,deviceScaleFactor:1});
  const sizes=[],scales=[];const display={getWidth:()=>1920,getHeight:()=>1080,scale:(s)=>scales.push(s)};resizeDisplay(stage,display,{sendSize:(...a)=>sizes.push(a)},{resolutionMode:'1920x1080',scaleMode:'125',resizeMode:'none'},false);assert.equal(sizes.length,0);assert.equal(scales.at(-1),1.25);
});

test('resize scheduler debounces',()=>{let id=0;const pending=new Map();const timers={setTimeout(cb){id++;pending.set(id,cb);return id;},clearTimeout(i){pending.delete(i);}};let runs=0;const s=createResizeScheduler(()=>runs++,150,timers);s.schedule();s.schedule();assert.equal(pending.size,1);const [i,cb]=[...pending.entries()][0];pending.delete(i);cb();assert.equal(runs,1);s.dispose();});

test('unicode text and clipboard pipeline work',()=>{
  const events=[];sendTextAsKeysyms({sendKeyEvent:(...a)=>events.push(a)},'A👋');assert.equal(events.length,4);
  let reader;class StringReader{constructor(){reader=this;this.ontext=null;this.onend=null;}}class StringWriter{constructor(){}sendText(t){events.push(t);}sendEnd(){events.push('end');}}
  const client={onclipboard:null,createClipboardStream:()=>({})};const cp=createClipboardPipeline({StringReader,StringWriter},client);client.onclipboard({},'text/plain');reader.ontext('remote');reader.onend();assert.equal(cp.readLatest(),'remote');cp.send('local');cp.dispose();assert.equal(client.onclipboard,null);
});

test('interaction defaults match mobile UX',()=>{const v=validateInteraction({});assert.equal(v.touchMode,'direct');assert.equal(v.gestureRightClick,true);assert.equal(v.longPressMs,500);assert.equal(v.scrollThreshold,20);assert.equal(v.resizeMode,'display-update');});

test('legacy resize flag maps to stable remote resolution',()=>{assert.equal(validateInteraction({resizeRemote:false}).resizeMode,'none');});
