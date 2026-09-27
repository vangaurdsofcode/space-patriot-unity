// Export spherical climate samples by executing the preserved HTML engines.
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '../..');
const source = path.join(root, 'Reference/Original/source');
const catalog = JSON.parse(fs.readFileSync(path.join(root, 'Reference/Original/assets/data/cosmoplot-worlds.json')));
const worlds = JSON.parse(fs.readFileSync(path.join(root, 'Assets/SpacePatriot/Resources/Worlds.json'))).worlds;
const records = new Map(catalog.systems.flatMap(s => s.planets.map(p => [p.id, { planet: p, system: s }])));
const types = { Mercury: 0, Venus: 1, Earth: 1, Mars: 2, Jupiter: 3, Saturn: 3, Uranus: 3, Neptune: 3,
  'TRAPPIST-1 b': 5, 'TRAPPIST-1 c': 1, 'TRAPPIST-1 d': 1, 'TRAPPIST-1 e': 1,
  'TRAPPIST-1 f': 4, 'TRAPPIST-1 g': 4, 'TRAPPIST-1 h': 0, 'TOI-270 b': 1,
  'TOI-270 c': 3, 'K2-141 b': 5, 'WASP-76 b': 3 };
const humidity = { Mercury: 0, Venus: 0, Earth: .90, Mars: 0, Jupiter: 0, Saturn: 0, Uranus: 0, Neptune: 0,
  'TRAPPIST-1 b': .47, 'TRAPPIST-1 c': .75, 'TRAPPIST-1 d': .75, 'TRAPPIST-1 e': .90,
  'TRAPPIST-1 f': .13, 'TRAPPIST-1 g': .13, 'TRAPPIST-1 h': 0, 'TOI-270 b': .75,
  'TOI-270 c': 0, 'K2-141 b': .03, 'WASP-76 b': 0 };
const WIDTH = 256, HEIGHT = 128;

const ctx = vm.createContext({ console });
const run = name => vm.runInContext(fs.readFileSync(path.join(source, name), 'utf8'), ctx, { filename: name });
run('core.js');
ctx.LongwayCore.Geology = { sample: () => ({ height: 0 }) };
run('landscape.js');
run('climate.js');
run('engines-worldworks.js');
run('engines-terrainworks.js');
run('planet-engines.js');

const out = path.join(root, 'Assets/SpacePatriot/Resources/Worldworks/Climate');
fs.mkdirSync(out, { recursive: true });
const report = [];
for (const world of worlds) {
  const record = records.get(world.id);
  if (!record) throw new Error(`Missing source catalog record for ${world.id}`);
  const type = types[record.planet.name];
  if (type === undefined) throw new Error(`Missing source biome type for ${record.planet.name}`);
  const seed = record.planet.worldSeed >>> 0;
  if (world.seed !== seed) throw new Error(`${world.id}: Unity/source seeds disagree`);
  const body = {
    id: `SP-${world.id}`, name: record.planet.name, seed, type, humidity: humidity[record.planet.name],
    radius: Math.max(160, Math.min(3600, Math.sqrt(record.planet.radiusEarth || 1) * 900)),
    atmosphere: type === 0 ? 0 : type === 3 ? .028 : .014,
    liquid: type === 1 ? 1 : 0, temperature: record.planet.name === 'Earth' ? 15 : (record.planet.equilibriumK ?? 250) - 273.15,
    catalog: record.planet, host: record.system, offset: [seed % 57, (seed >>> 8) % 59, (seed >>> 16) % 61],
  };
  const bytes = Buffer.alloc(16 + WIDTH * HEIGHT * 16);
  bytes.writeInt32LE(WIDTH, 0); bytes.writeInt32LE(HEIGHT, 4); bytes.writeUInt32LE(seed, 8); bytes.writeFloatLE(1, 12);
  const sums = [0, 0, 0, 0], mins = [1, 1, 1, 1], maxs = [0, 0, 0, 0];
  for (let y = 0; y < HEIGHT; y++) {
    const latitude = (y / (HEIGHT - 1) - .5) * Math.PI;
    const cl = Math.cos(latitude), sl = Math.sin(latitude);
    for (let x = 0; x < WIDTH; x++) {
      const longitude = (x / WIDTH - .5) * Math.PI * 2;
      const n = [cl * Math.cos(longitude), sl, cl * Math.sin(longitude)];
      const r = ctx.LongwayCore.PlanetEngines.region(body, n, 0);
      const values = [r.moisture, r.temperature, r.exposure, r.forestCover];
      const offset = 16 + (y * WIDTH + x) * 16;
      values.forEach((v, c) => {
        const value = Math.max(0, Math.min(1, Number(v)));
        bytes.writeFloatLE(value, offset + c * 4); sums[c] += value; mins[c] = Math.min(mins[c], value); maxs[c] = Math.max(maxs[c], value);
      });
    }
  }
  fs.writeFileSync(path.join(out, `${world.id}.bytes`), bytes);
  report.push({ id: world.id, name: body.name, seed, width: WIDTH, height: HEIGHT,
    climate: ['moisture', 'temperature', 'exposure', 'forestCover'].map((name, i) => ({ name, min: mins[i], max: maxs[i], mean: sums[i] / (WIDTH * HEIGHT) })) });
  console.log(`Exported ${world.id}: moisture ${mins[0].toFixed(2)}–${maxs[0].toFixed(2)}, exposure ${mins[2].toFixed(2)}–${maxs[2].toFixed(2)}`);
}
fs.writeFileSync(path.join(root, 'Validation/planet-climate-export.json'), JSON.stringify(report, null, 2) + '\n');
