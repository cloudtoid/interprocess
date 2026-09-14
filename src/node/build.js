'use strict';
const { execFileSync } = require('node:child_process');
const { copyFileSync } = require('node:fs');
const { resolve, join } = require('node:path');
const root = resolve(__dirname, '../..');
execFileSync('cargo', ['build', '--release', '--locked', '-p', 'cloudtoid-interprocess-node'], { cwd: root, stdio: 'inherit' });
const library = process.platform === 'win32' ? 'cloudtoid_interprocess_node.dll' : process.platform === 'darwin' ? 'libcloudtoid_interprocess_node.dylib' : 'libcloudtoid_interprocess_node.so';
copyFileSync(join(process.env.CARGO_TARGET_DIR || join(root, 'target'), 'release', library), join(__dirname, `interprocess.${process.platform}-${process.arch}.node`));
