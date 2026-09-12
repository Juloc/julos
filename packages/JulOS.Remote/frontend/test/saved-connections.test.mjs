import assert from 'node:assert/strict';
import test from 'node:test';
import {decodeRemoteLaunchTarget,encodeRemoteLaunchTarget,parseTarget} from '../remote.source.js';

test('saved target contains presentation settings but no password',()=>{
 const target={protocol:'rdp',host:'windows.home.arpa',port:3389,userName:'julian',domain:'HOME',secretReferenceId:'11111111-1111-4111-8111-111111111111',interaction:{touchMode:'direct',gestureRightClick:true,longPressMs:500,scrollThreshold:20,cursorVisible:true,resolutionMode:'1920x1080',customWidth:1920,customHeight:1080,scaleMode:'fit',resizeMode:'display-update',toolbarExpanded:false}};
 const id=encodeRemoteLaunchTarget(target);assert.match(id,/^remote:v1:/u);assert.equal(id.includes('password'),false);assert.deepEqual(decodeRemoteLaunchTarget(id),target);
});

test('legacy targets receive safe defaults',()=>{const id=encodeRemoteLaunchTarget({protocol:'ssh',host:'debian.home.arpa',port:22,userName:'admin',domain:'',secretReferenceId:null});const decoded=decodeRemoteLaunchTarget(id);assert.equal(decoded.secretReferenceId,null);assert.equal(decoded.interaction.touchMode,'direct');assert.equal(decoded.interaction.longPressMs,500);assert.equal(decoded.interaction.resizeMode,'display-update');});

test('target parser defaults ports',()=>{assert.deepEqual(parseTarget('server.home.arpa','rdp'),{host:'server.home.arpa',port:3389});assert.deepEqual(parseTarget('server.home.arpa:2222','ssh'),{host:'server.home.arpa',port:2222});assert.deepEqual(parseTarget('[2001:db8::10]:5901','vnc'),{host:'2001:db8::10',port:5901});});
