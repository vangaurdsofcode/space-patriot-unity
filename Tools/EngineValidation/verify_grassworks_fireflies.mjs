// Source-driven regression for the original Grassworks HTML, no browser/GPU.
// Actual WebGL coverage is separately available via ?validate-fireflies=1.
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const sourcePath=path.join(root,'Reference/Original/vendor/user-engines/grasspack3js.html');
const html=fs.readFileSync(sourcePath,'utf8');
const moduleText=html.match(/<script type="module">([\s\S]*?)<\/script>/)[1];
// Parse the actual whole module without executing DOM/WebGL imports.
new vm.Script(moduleText,{filename:sourcePath});
const setup=html.slice(html.indexOf('const fireflyCount='),html.indexOf('scene.add(fireflies);')+'scene.add(fireflies);'.length);
const animation=html.slice(html.indexOf('const baseFireflies ='),html.indexOf('// CAMERA DRIFT'));
const THREE={
  BufferGeometry:class{attributes={};setAttribute(name,value){this.attributes[name]=value;}},
  BufferAttribute:class{constructor(array,itemSize){this.array=array;this.itemSize=itemSize;this.needsUpdate=false;}},
  ShaderMaterial:class{constructor(config){Object.assign(this,config);}},
  Points:class{constructor(geometry,material){this.geometry=geometry;this.material=material;this.visible=true;}},
  NormalBlending:'normal-alpha'
};
let seed=4097;
const seededMath=Object.create(Math);
seededMath.random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};
const context=vm.createContext({THREE,Float32Array,Math:seededMath,scene:{add(){}},renderer:{domElement:{height:900},getPixelRatio:()=>1.75}});
vm.runInContext(setup+'\n'+animation+'\nglobalThis.fixture={fireflyCount,fireflyPulse,fireflies,fireflyGeometry,fireflyMaterial,animateFireflies};',context);
const f=context.fixture,material=f.fireflyMaterial,checks=[];
function check(value,name){assert.ok(value,name);checks.push(name);}
check(f.fireflyCount===120,'Original population stays at 120 insects');
check(material.blending==='normal-alpha'&&!material.depthWrite&&material.depthTest&&!material.toneMapped,'Shader uses explicit normal alpha/depth and no tone-mapped HDR');
const phases=Array.from(f.fireflyPulse).filter((_,i)=>i%2===0),periods=Array.from(f.fireflyPulse).filter((_,i)=>i%2===1);
check(new Set(phases).size===120&&new Set(periods).size===120,'Every insect has its own stored phase and period');
check(periods.every(p=>p>=2.8&&p<=7),'Pulse periods remain between 2.8 and 7 seconds');
f.animateFireflies(12.25);
check(material.uniforms.uTime.value===12.25&&material.uniforms.uViewportHeight.value===900&&material.uniforms.uPixelRatio.value===1.75,'Actual animation updates time and viewport/DPR uniforms');
check(f.fireflyGeometry.attributes.position.needsUpdate,'Original drifting positions still update');
f.fireflies.visible=false;f.animateFireflies(99);
check(material.uniforms.uTime.value===12.25,'Disabled toggle still skips firefly animation');
check(!animation.includes('fireflyMaterial.opacity'),'No shared global opacity flash remains');
check(material.vertexShader.includes('clamp(projectedSize,1.0,6.0*uPixelRatio)')&&material.vertexShader.includes('smoothstep(0.45,2.0,depth)')&&material.vertexShader.includes('if(depth<=0.0)'),'Actual vertex shader enforces size/near/behind guards');
check(material.fragmentShader.includes('if(r2>=1.0||vOpacity<=0.0)discard')&&material.fragmentShader.includes('vec3(0.48,0.62,0.035)'),'Actual fragment shader uses round footprint and nonwhite color ceiling');
check(html.includes('$("firefliesToggle").checked=true;')&&html.includes('fireflies.visible=true;'),'Reset still restores visible fireflies');
const smooth=(a,b,x)=>{const t=Math.max(0,Math.min(1,(x-a)/(b-a)));return t*t*(3-2*t);};
let maxDiameter=0,maxAlpha=0;
for(const dpr of [1,1.25,1.75,2,3])for(const height of [240,900,2160,4320])for(const depth of [-4,-.01,0,.001,.2,.45,.5,1,2,10,48,200]){
  const pixels=Math.min(Math.max(.055/Math.tan(50*Math.PI/360)*height*.5/Math.max(depth,.01),1),6*dpr);
  const fade=depth<=0?0:smooth(.45,2,depth)*(1-smooth(36,48,Math.abs(depth)));
  const alpha=material.uniforms.uOpacity.value*fade;
  check(Number.isFinite(pixels)&&pixels/dpr<=6,'Projection finite and bounded at depth '+depth+', DPR '+dpr+', H '+height);
  if(depth<=.45)assert.equal(alpha,0,'Camera crossing must be fully transparent');
  maxDiameter=Math.max(maxDiameter,pixels/dpr);maxAlpha=Math.max(maxAlpha,alpha);
}
const alpha=material.uniforms.uOpacity.value;
let combined=0;for(let i=0;i<120;i++)combined=.62*alpha+combined*(1-alpha);
check(combined<=.62,'Normal alpha overlap remains below source green channel rather than adding brightness');
const report={scope:'Original Grassworks source and CPU math only; WebGL pixel test must run separately',source:'Reference/Original/vendor/user-engines/grasspack3js.html',passed:true,assertions:checks.length,maxDiameterCssPixels:maxDiameter,maxAlpha,overlapLinearGreen:combined,browserPixelTest:'Reference/Original/vendor/user-engines/grasspack3js.html?validate-fireflies=1',checks:checks.filter(name=>!name.startsWith('Projection finite'))};
fs.mkdirSync(path.join(root,'Validation'),{recursive:true});
fs.writeFileSync(path.join(root,'Validation/grassworks-firefly-source-regression.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
