import { existsSync, readFileSync } from 'node:fs';
import type { Plugin } from 'vite';
import { defineConfig } from 'vitest/config';

const BUNDLED = ['three', 'polygon-clipping', 'robust-predicates', 'splaytree'];

/** The licence of a package the bundle includes: its licence file, or what its package.json declares. */
function licenceOf(name: string): string {
  const root = new URL(`node_modules/${name}/`, import.meta.url);
  const pkg = JSON.parse(readFileSync(new URL('package.json', root), 'utf8')) as {
    version: string;
    license: string;
    author?: string | { name: string };
  };
  const file = ['LICENSE', 'LICENSE.md', 'LICENSE.txt'].map(f => new URL(f, root)).find(u => existsSync(u));
  const author = typeof pkg.author === 'string' ? pkg.author : pkg.author?.name;
  const text = file
    ? readFileSync(file, 'utf8').trim()
    : `${pkg.license} licence, as declared in its package.json${author ? `; Copyright ${author}` : ''}.`;
  return [`${name} ${pkg.version} (${pkg.license})`, '', text].join('\n');
}

/** Ships the licences of the packages bundled into the view next to the bundle. */
function licences(): Plugin {
  return {
    name: 'licences',
    generateBundle() {
      this.emitFile({
        type: 'asset',
        fileName: 'THIRD-PARTY-LICENSES.txt',
        source: BUNDLED.map(licenceOf).join(`\n\n${'-'.repeat(72)}\n\n`) + '\n'
      });
    }
  };
}

export default defineConfig({
  publicDir: false,
  plugins: [licences()],
  build: {
    outDir: '../StormBench.Scada/wwwroot/live/dist',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: true,
    modulePreload: false,
    chunkSizeWarningLimit: 1024,
    rolldownOptions: {
      input: 'src/index.ts',
      preserveEntrySignatures: 'strict',
      output: {
        entryFileNames: 'scene.js',
        format: 'es',
        postBanner:
          '/*! StormBench Live 3D view. Includes three.js r180 (MIT), polygon-clipping (MIT), robust-predicates (Unlicense) and splaytree (MIT): THIRD-PARTY-LICENSES.txt */'
      }
    }
  },
  test: {
    include: ['test/**/*.test.ts'],
    environment: 'node'
  }
});
