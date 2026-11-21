import { loadEnvFile } from 'node:process';
import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';

/** Reads the user's ignored credential file into this short-lived process only. */
loadEnvFile('.dev.vars');
/** Restricts upload to the two intended Google credentials, passing values through stdin. */
const credentials = Object.fromEntries(['GOOGLE_CLIENT_ID','GOOGLE_CLIENT_SECRET'].map(name => {
  const value = process.env[name];
  if (!value) throw new Error(`Missing ${name} in .dev.vars`);
  return [name,value];
}));
/** Wrangler receives JSON on stdin; no credential appears in command arguments or logs. */
const result = spawnSync(process.execPath,[resolve('node_modules/wrangler/bin/wrangler.js'),'secret','bulk'],{
  input:JSON.stringify(credentials),stdio:['pipe','inherit','inherit'],env:process.env,
});
process.exitCode = result.status ?? 1;
