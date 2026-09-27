// Executes the user's unmodified Worldworks and Terrainworks cores at build time.
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
const base=path.resolve(import.meta.dirname,'../..'), source=base+'/Reference/Original/source/';
const c=vm.createContext({console});
for(const f of ['engines-worldworks.js','engines-terrainworks.js'])vm.runInContext(fs.readFileSync(source+f,'utf8'),c);
const catalog=JSON.parse(fs.readFileSync(base+'/Reference/Original/assets/data/cosmoplot-worlds.json'));
const originals=catalog.systems.flatMap(s=>s.planets);
const systemByWorld=new Map(catalog.systems.flatMap(s=>s.planets.map(p=>[p.id,s])));
const worlds=JSON.parse(fs.readFileSync(base+'/Assets/SpacePatriot/Resources/Worlds.json')).worlds;
// Keep Unity's playable catalog on the original living.js biome assignment.
// These are source world identities, not broad visual guesses based on the
// reduced biome label that was previously stored in Worlds.json.
const biomeByName={Mercury:6,Venus:1,Earth:7,Mars:1,Jupiter:12,Saturn:12,Uranus:12,Neptune:12,'TRAPPIST-1 b':3,'TRAPPIST-1 c':1,'TRAPPIST-1 d':1,'TRAPPIST-1 e':7,'TRAPPIST-1 f':10,'TRAPPIST-1 g':10,'TRAPPIST-1 h':6,'TOI-270 b':1,'TOI-270 c':12,'K2-141 b':3,'WASP-76 b':12};
// [underlying terrain type, original living.js humidity].
const biomeProfiles=[[1,.65],[2,.10],[4,.23],[5,.01],[1,.82],[1,.91],[0,0],[1,.90],[5,.47],[1,.75],[4,.13],[2,.34],[3,.42],[5,.03]];
const out=base+'/Assets/SpacePatriot/Resources/Worldworks';fs.mkdirSync(out,{recursive:true});
const report=[];
for(const w of worlds){
 const source=originals.find(b=>b.id===w.id);if(!source)throw new Error('Missing original source world '+w.id);
 const host=systemByWorld.get(w.id);if(!host)throw new Error('Missing host star for '+w.id);
 w.stellarTemperatureK=host.stellar?.effectiveTemperatureK||5772;
 const biomeIndex=biomeByName[source.name];if(biomeIndex===undefined)throw new Error('Missing original living.js biome for '+source.name);
 const [type,wet]=biomeProfiles[biomeIndex],seed=source.worldSeed>>>0;
 if(w.seed!==seed)throw new Error(`${w.id}: Unity world seed ${w.seed} differs from original ${seed}; update Worlds.json before exporting.`);
 const sourceBiome=['rock','temperate','desert','gas','ice','volcanic'][type];
 if(w.biome!==sourceBiome)throw new Error(`${w.id}: Unity biome ${w.biome} differs from original ${sourceBiome}; update Worlds.json before exporting.`);
 if(type===3){for(const suffix of ['.bytes','.bytes.meta']){const stale=out+'/'+w.id+suffix;if(fs.existsSync(stale))fs.unlinkSync(stale);}continue;}
 // Five overlapping source-engine provinces prevent the port's one-valley
 // look. Each is a complete Worldworks + Terrainworks profile; climate and
 // landform changes share the same soft Voronoi borders.
 const baseRelief=[22,26,19,0,31,34][type],styles={
  0:['highlands','alpine','islands','highlands','desert'],
  1:['highlands','islands','alpine','highlands','desert'],
  2:['desert','highlands','islands','desert','alpine'],
  4:['alpine','highlands','islands','alpine','desert'],
  5:['alpine','highlands','desert','alpine','islands']
 }[type]||['highlands','islands','alpine','desert','highlands'];
 const rng=(()=>{let a=seed>>>0;return()=>{a+=0x6D2B79F5;let t=a;t=Math.imul(t^t>>>15,t|1);t^=t+Math.imul(t^t>>>7,t|61);return((t^t>>>14)>>>0)/4294967296;};})();
 const provinces=styles.map((style,index)=>{
  const localSeed=(seed+Math.imul(index+1,0x9E3779B9))>>>0,relief=baseRelief*([.82,1.2,.58,1.05,.72][index]);
  const temperature=Math.max(0,Math.min(1,(type===4?.1:type===2?.9:.55)+[-.22,.15,-.08,.1,.24][index]));
  const moisture=Math.max(0,Math.min(1,wet+[-.22,.12,.28,-.1,-.3][index]));
  const doc=c.WWCore.create(localSeed,style,relief,128,180);
  const terrain=c.TerrainworksCore.create({seed:localSeed,mountainHeight:relief,temperature,moisture,terracing:type===2?.7:.12,warp:[.55,1.15,.9,.72,1.35][index],ridgeSharpness:[3.2,1.6,2.5,2.1,1.2][index],erosion:[1.9,4.2,2.7,3.3,5.1][index],valleyDepth:[3.5,8,5,7,9][index],biomeScale:[1.8,3.1,2.3,2.7,3.7][index],rockCluster:[1.3,.55,.9,1.1,.45][index]});
  return {style,relief,temperature,moisture,doc,terrain,x:(rng()-.5)*152,z:(rng()-.5)*152};
 });
 const data=Buffer.alloc(16+129*129*16);data.writeInt32LE(129);data.writeFloatLE(5760,4);data.writeUInt32LE(seed,8);data.writeFloatLE(provinces[0].doc.env.water,12);
 let min=Infinity,max=-Infinity,provinceSamples=Array(styles.length).fill(0);
 for(let j=0;j<129;j++)for(let i=0;i<129;i++){
  const x=(i/128-.5)*180,z=(j/128-.5)*180,k=j*129+i;
  const ranked=provinces.map((p,index)=>({p,index,d:Math.hypot(x-p.x,z-p.z)})).sort((a,b)=>a.d-b.d);
  const a0=ranked[0],a1=ranked[1],blend=Math.max(0,Math.min(1,(a1.d-a0.d+9)/18)),w=blend*blend*(3-2*blend);
  const sample=q=>{const m=q.p.terrain.moisture(x,z),r=q.p.terrain.rocks(x,z),mix=Math.max(.38,Math.min(.82,.5+r*.35-m*.15));return [(q.p.doc.base[k]*(1-mix)+q.p.terrain.height(x,z)*mix-q.p.doc.env.water)*32,m,q.p.terrain.temperature(x,z,0),r];};
  const va=sample(a0),vb=sample(a1),values=va.map((v,n)=>v*(1-w)+vb[n]*w),height=values[0];
  provinceSamples[a0.index]++;provinceSamples[a1.index]++;
  values.forEach((v,n)=>data.writeFloatLE(v,16+k*16+n*4));min=Math.min(min,height);max=Math.max(max,height);
 }
 fs.writeFileSync(out+'/'+w.id+'.bytes',data);report.push({world:w.id,sourceName:source.name,seed,type,humidity:wet,styles,provinces:provinces.map((p,i)=>({style:p.style,seed:(seed+Math.imul(i+1,0x9E3779B9))>>>0,temperature:p.temperature,moisture:p.moisture,contributingSamples:provinceSamples[i]})),min,max,samples:16641});
}
fs.writeFileSync(base+'/Assets/SpacePatriot/Resources/Worlds.json',JSON.stringify({worlds},null,2)+'\n');
fs.writeFileSync(base+'/Validation/worldworks-export.json',JSON.stringify(report,null,2));console.log('Compiled original Worldworks + Terrainworks:',report.length,'worlds with five blended geology/climate provinces each; copied stellar pigments for',worlds.length,'worlds');
