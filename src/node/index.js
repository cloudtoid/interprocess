'use strict';
const { existsSync } = require('node:fs');
const { join } = require('node:path');
const target = `${process.platform}-${process.arch}`;
const local = join(__dirname, `interprocess.${target}.node`);
module.exports = existsSync(local) ? require(local) : require(`@cloudtoid/interprocess-${target}`);
