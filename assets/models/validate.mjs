import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import validator from 'gltf-validator';

const here = dirname(fileURLToPath(import.meta.url));
const models = join(here, '..', '..', 'src', 'StormBench.Scada', 'wwwroot', 'live', 'models');

/** Validates every GLB in a folder with the Khronos glTF Validator; any error fails the run. */
export async function validateAll(folder) {
  const files = readdirSync(folder).filter(f => f.endsWith('.glb')).sort();
  if (files.length === 0) throw new Error(`no models in ${folder}`);
  let failed = 0;
  for (const file of files) {
    const report = await validator.validateBytes(new Uint8Array(readFileSync(join(folder, file))), { uri: file, format: 'glb' });
    const { numErrors, numWarnings } = report.issues;
    const status = numErrors > 0 ? 'FAIL' : 'ok';
    console.log(`${status.padEnd(4)} ${file}  ${numErrors} errors, ${numWarnings} warnings`);
    for (const message of report.issues.messages.filter(m => m.severity === 0)) {
      console.log(`     ${message.code} ${message.pointer ?? ''} ${message.message}`);
    }
    if (numErrors > 0) failed++;
  }
  if (failed > 0) throw new Error(`${failed} of ${files.length} models are invalid`);
}

if (import.meta.url === pathToFileURL(resolve(process.argv[1] ?? '')).href) {
  await validateAll(models);
}
