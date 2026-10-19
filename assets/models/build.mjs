import { spawnSync } from 'node:child_process';
import { mkdirSync, readdirSync, rmSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateAll } from './validate.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const staging = join(here, '.export');
const target = join(here, '..', '..', 'src', 'StormBench.Scada', 'wwwroot', 'live', 'models');
const blender = process.env.BLENDER ?? 'blender';

/** Runs a command, streaming its output, and stops the build when it fails. */
function run(command, args) {
  const result = spawnSync(command, args, { stdio: 'inherit', shell: process.platform === 'win32' && command === 'npx' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} exited with ${result.status}`);
}

/** File name on the web: TreatmentPlant becomes treatment-plant. */
function webName(collection) {
  return collection.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
}

rmSync(staging, { recursive: true, force: true });
mkdirSync(staging, { recursive: true });
mkdirSync(target, { recursive: true });
run(blender, ['--background', join(here, 'stormbench.blend'), '--python', join(here, 'export.py'), '--', staging]);
for (const file of readdirSync(staging).filter(f => f.endsWith('.glb'))) {
  const out = join(target, `${webName(basename(file, '.glb'))}.glb`);
  run('npx', [
    'gltf-transform', 'optimize', join(staging, file), out,
    '--compress', 'meshopt',
    '--texture-compress', 'false',
    '--join', 'false',
    '--flatten', 'false',
    '--instance', 'false',
    '--palette', 'false',
    '--prune', 'false',
    '--simplify', 'false'
  ]);
}
rmSync(staging, { recursive: true, force: true });
await validateAll(target);
